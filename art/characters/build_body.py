"""Builds a player body: game/assets/characters/human_<body>.glb, its textures and human_<body>.json.

    set BLENDER_USER_RESOURCES=D:\\GameDev\\Tools\\blender-hollow-config
    blender -b --factory-startup --python art/characters/build_body.py                    # the male
    blender -b --factory-startup --python art/characters/build_body.py -- --body female   # the female

What it does:
1. MakeHuman male from MPFB 2 (macro values in hollow_art.MALE), MPFB's "game_engine" skeleton (53 bones, UE-style
   names) and its weights. Rest pose: MakeHuman's A-pose. The animation script retargets every clip onto this rig.
2. Eyes, eyebrows, eyelashes and five hair styles from the MakeHuman CC0 system assets, fitted and weighted by MPFB
   (hair_<style>_lod0/1). A player's look (GROUND.md §4.8) picks one of them, or none ("buzz": the painted hair base).
3. Faces: five shape keys on the nearest body (face_1 … face_5) made from MakeHuman's face targets and its race
   macros, kept to the lower and middle face (nose, cheeks, mouth, jaw, chin), so the eyes, brows, lashes, hair and
   every garment fit them all. The base face is the sixth.
4. Skin tone: the light skin texture and a dark one (its painted scalp hair replaced by skin) are blended per player in
   the shader; a mask texture carries the hair base painted on the scalp and the stubble, coloured per player.
5. Every body face gets a region (hollow_art.REGIONS); clothing hides regions it covers, so nothing pokes through.
6. Underwear is part of the body: the pelvis region, smoothed, lifted 3 mm and given its own fabric surface.
7. Levels of detail by collapse decimation: about 18k / 7k / 2.4k triangles for the body.
8. Saves art/.cache/human_<body>.blend and human_<body>_faces.npz (the faces' offsets of the full body) for the
   clothing and animation scripts.
"""
import math
import os
import shutil
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "lib"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import hollow_art as H  # noqa: E402
import skin_maps  # noqa: E402
import surfaces as S  # noqa: E402

# For each part: (target triangles or None for full detail, levels of detail it is drawn at). Every hair style is
# drawn as "hair".
LODS = {
    "body": [(18000, [0]), (7000, [1]), (2400, [2])],
    "eyes": [(None, [0]), (300, [1, 2])],
    "brows": [(None, [0, 1])],
    "lashes": [(None, [0])],
    "hair": [(None, [0]), (1100, [1])],
}
PARTS = {
    "eyes": ("Eyes", "eyes/high-poly/high-poly.mhclo"),
    "brows": ("Eyebrows", "eyebrows/eyebrow001/eyebrow001.mhclo"),
    "lashes": ("Eyelashes", "eyelashes/eyelashes01/eyelashes01.mhclo"),
}
MATERIAL_OF = {"eyes": "eye", "brows": "brow", "lashes": "lash"}

# Face shapes (MakeHuman targets, weight). "race:<name>" is the difference between that race's macro target and the
# body's own mix, so a face can lean African or Asian without changing anything but the face.
MALE_FACES = [
    ("face_1", [("race:african", 0.8), ("nose/nose-flaring-incr", 0.2), ("mouth/mouth-lowerlip-volume-incr", 0.25)]),
    ("face_2", [("race:asian", 0.85), ("cheek/l-cheek-bones-incr", 0.25), ("cheek/r-cheek-bones-incr", 0.25)]),
    ("face_3", [("cheek/l-cheek-bones-incr", 0.6), ("cheek/r-cheek-bones-incr", 0.6), ("chin/chin-prominent-incr", 0.5),
                ("nose/nose-hump-incr", 0.7), ("nose/nose-scale-vert-incr", 0.3), ("cheek/l-cheek-volume-decr", 0.4),
                ("cheek/r-cheek-volume-decr", 0.4), ("chin/chin-width-decr", 0.2)]),
    ("face_4", [("cheek/l-cheek-volume-incr", 0.7), ("cheek/r-cheek-volume-incr", 0.7), ("chin/chin-width-incr", 0.5),
                ("nose/nose-point-up", 0.5), ("nose/nose-scale-horiz-incr", 0.3), ("nose/nose-volume-incr", 0.3),
                ("mouth/mouth-upperlip-volume-incr", 0.3), ("chin/chin-height-decr", 0.3)]),
    ("face_5", [("chin/chin-height-incr", 0.6), ("nose/nose-scale-vert-incr", 0.5), ("nose/nose-curve-convex", 0.6),
                ("nose/nose-point-down", 0.3), ("cheek/l-cheek-inner-decr", 0.5), ("cheek/r-cheek-inner-decr", 0.5),
                ("mouth/mouth-scale-horiz-decr", 0.3), ("chin/chin-prognathism-incr", 0.2)]),
]
FEMALE_FACES = [
    ("face_1", [("race:african", 0.8), ("mouth/mouth-upperlip-volume-incr", 0.3), ("mouth/mouth-lowerlip-volume-incr", 0.3)]),
    ("face_2", [("race:asian", 0.85)]),
    ("face_3", [("cheek/l-cheek-bones-incr", 0.5), ("cheek/r-cheek-bones-incr", 0.5), ("nose/nose-scale-horiz-decr", 0.3),
                ("nose/nose-point-up", 0.4), ("chin/chin-prominent-incr", 0.3), ("mouth/mouth-upperlip-volume-incr", 0.3)]),
    ("face_4", [("cheek/l-cheek-volume-incr", 0.6), ("cheek/r-cheek-volume-incr", 0.6), ("chin/chin-width-incr", 0.4),
                ("nose/nose-volume-incr", 0.3), ("chin/chin-height-decr", 0.3), ("mouth/mouth-scale-horiz-incr", 0.2)]),
    ("face_5", [("chin/chin-height-incr", 0.4), ("nose/nose-scale-vert-incr", 0.4), ("nose/nose-hump-incr", 0.5),
                ("cheek/l-cheek-inner-decr", 0.4), ("cheek/r-cheek-inner-decr", 0.4), ("mouth/mouth-scale-horiz-decr", 0.2)]),
]

# The two bodies. Their skin and brows have their own materials (the suffix) and textures; eyes, lashes, the hair styles
# and the underwear's fabric are shared. Hair styles are MakeHuman's (hair/<style>/<style>.mhclo); the first is the
# default look's.
SKINS = "skins/"
PROFILES = {
    "male": {"suffix": "", "macros": "MALE", "targets": "MALE_TARGETS", "stubble": True, "top": False, "gender": "male",
             "skin": SKINS + "young_caucasian_male/young_lightskinned_male_diffuse.png",
             "skin_dark": SKINS + "young_african_male/young_darkskinned_male_diffuse.png",
             "brows": "eyebrows/eyebrow001/eyebrow001",
             "hairs": ["short02", "short01", "short03", "short04", "afro01"], "faces": MALE_FACES},
    "female": {"suffix": "_female", "macros": "FEMALE", "targets": "FEMALE_TARGETS", "stubble": False, "top": True,
               "gender": "female",
               "skin": SKINS + "young_caucasian_female/young_lightskinned_female_diffuse.png",
               "skin_dark": SKINS + "young_african_female/young_darkskinned_female_diffuse.png",
               "brows": "eyebrows/eyebrow006/eyebrow006",
               "hairs": ["ponytail01", "bob01", "bob02", "short03", "afro01"], "faces": FEMALE_FACES},
}


def main():
    a = H.args()
    out = os.path.abspath(a.get("out", H.CHARACTERS))
    H.fresh_scene()
    H.enable_mpfb()
    HumanService, TargetService, LocationService = H.mpfb()

    which = a.get("body", "male")
    profile = PROFILES[which]
    body, rig = H.make_human(getattr(H, profile["macros"]), getattr(H, profile["targets"]))
    rig.name = rig.data.name = "Rig"
    body.name = body.data.name = "body"
    parts = {}
    data = LocationService.get_user_data()
    for key, (kind, rel) in PARTS.items():
        if key == "brows":
            rel = profile[key] + ".mhclo"
        obj = HumanService.add_mhclo_asset(os.path.join(data, rel), body, asset_type=kind, subdiv_levels=0,
                                           material_type="MAKESKIN")
        obj.name = obj.data.name = key
        parts[key] = obj
    for style in profile["hairs"]:
        obj = HumanService.add_mhclo_asset(os.path.join(data, "hair", style, style + ".mhclo"), body, asset_type="Hair",
                                           subdiv_levels=0, material_type="MAKESKIN")
        obj.name = obj.data.name = "hair_" + style
        parts["hair_" + style] = obj
    H.log("human built:", {k: H.triangles(o) for k, o in parts.items()})

    TargetService.bake_targets(body)
    blink = blink_offsets(body, TargetService, LocationService)
    eye_centre = bbox_centre(parts["eyes"])
    faces = face_offsets(body, rig, parts["eyes"], profile, TargetService, LocationService)
    remove_helpers(body)
    for obj in [body] + list(parts.values()):
        clean(obj, rig)

    # A crop top runs from under the bust to above it; its edges are cut straight like the briefs'.
    top_band = bust_band(body, rig, eye_centre) if profile["top"] else ()
    cut_underwear_edges(body, rig, eye_centre, top_band)
    regions = classify(body, rig, eye_centre)
    write_regions(body, regions)
    top = crop_top(body, regions, top_band) if profile["top"] else set()
    shape_underwear(body, regions, rig, top)
    assign_body_materials(body, regions, top, profile["suffix"])
    for key, obj in parts.items():
        material = "hair_" + key[5:] if key.startswith("hair_") else MATERIAL_OF[key] + (profile["suffix"] if key == "brows" else "")
        set_single_material(obj, material)
        H.set_region_uv(obj, [H.REGION["face"]] * len(obj.data.polygons))

    skin = "skin_" + which
    folder = os.path.join(out, "textures")
    textures(out, profile)
    look_textures(body, regions, eye_centre, folder, skin, profile)
    skin_maps.paint(body, folder, skin)

    # Levels of detail.
    lods, levels = {}, {}
    for key, obj in [("body", body)] + list(parts.items()):
        for target, drawn in LODS["hair" if key.startswith("hair_") else key]:
            name = "%s_lod%d" % (key, drawn[0])
            copy = H.duplicate(obj, name)
            levels[name] = drawn
            if target is not None:
                tris = H.triangles(copy)
                if target < tris:
                    H.decimate(copy, target / tris)
            if key == "body":
                write_regions_from_attribute(copy)
            H.limit_and_normalise_weights(copy)
            lods[name] = copy
        bpy.data.objects.remove(obj) if key != "body" else None
    counts = {name: H.triangles(o) for name, o in lods.items()}
    H.log("levels of detail:", counts)
    for name in ("body_lod0", "lashes_lod0"):
        add_shape(lods[name], "blink", blink)
    for face, offsets in faces:
        add_shape(lods["body_lod0"], face, offsets)

    # The cache keeps the full-resolution body with its regions, for the clothing script, and the faces' offsets of
    # its vertices (the clothing round the face fits every face).
    body.name = "body_source"
    body.hide_render = True
    os.makedirs(H.CACHE, exist_ok=True)
    cache = os.path.join(H.CACHE, "human_%s.blend" % which)
    bpy.ops.wm.save_as_mainfile(filepath=cache, check_existing=False, compress=True)
    H.log("saved", os.path.relpath(cache, H.REPO))
    np.savez_compressed(os.path.join(H.CACHE, "human_%s_faces.npz" % which),
                        **{face: shape_offsets(body, offsets) for face, offsets in faces})

    H.export_glb(os.path.join(out, "human_%s.glb" % which), [rig] + list(lods.values()), morphs=True)
    bones = {b.name: [round(x, 4) for x in (rig.matrix_world @ b.head_local)] for b in rig.data.bones}
    H.write_json(os.path.join(out, "human_%s.json" % which), {
        "source": "MakeHuman (MPFB 2.0.17, CC0 base mesh and system assets), art/characters/build_body.py",
        "height_m": round(body.dimensions.z, 3),
        "eye_height_m": round(eye_centre.z, 3),
        "regions": H.REGIONS,
        "meshes": {name: {"lods": levels[name], "triangles": counts[name]} for name in lods},
        "hair_styles": profile["hairs"],
        "faces": [face for face, _ in faces],
        "bones": len(rig.data.bones),
        "bone_heads_blender": bones,
    })
    if a.get("preview"):
        import preview
        preview.render(a["preview"], [rig] + [o for n, o in lods.items() if n.endswith("lod0")], out)


def face_offsets(body, rig, eyes, profile, TargetService, LocationService):
    """The face shapes (profile["faces"]), measured on a copy of the body while it still has the base mesh's vertex
    order: for each, the world positions of the vertices that move and how far. Every target's offset is kept to the
    lower and middle face: nothing moves round the eyes, on the brows, the forehead, the scalp, the ears or below the
    jaw, so the eyes, brows, lashes and hair (fitted to the base face) and the garments fit every face."""
    targets = LocationService.get_mpfb_data("targets")
    races = ("african", "asian", "caucasian")
    gender = profile["gender"]
    needed = sorted({t for _, recipe in profile["faces"] for t, _ in recipe if not t.startswith("race:")})
    needed += ["macrodetails/%s-%s-young" % (r, gender) for r in races]
    probe = H.duplicate(body, "face_probe")
    for i, rel in enumerate(needed):
        TargetService.load_target(probe, os.path.join(targets, rel + ".target.gz"), weight=1.0, name="face_t%d" % i)
    keys = probe.data.shape_keys.key_blocks
    basis = np.array([v.co[:] for v in keys[0].data], np.float64)
    m = np.array(probe.matrix_world)
    world = basis @ m[:3, :3].T + m[:3, 3]
    delta = {}
    for i, rel in enumerate(needed):
        kb = keys["face_t%d" % i]
        delta[rel] = (np.array([v.co[:] for v in kb.data], np.float64) - basis) @ m[:3, :3].T
    bpy.data.objects.remove(probe)

    # A race's face: its macro target less the body's own mix (the young targets carry most of the age's weight).
    mix = getattr(H, profile["macros"])["race"]
    young = 1.0 - max(0.0, getattr(H, profile["macros"])["age"] - 0.5) / 0.5
    own = sum(mix[r] * delta["macrodetails/%s-%s-young" % (r, gender)] for r in races)
    for r in races:
        delta["race:" + r] = young * (delta["macrodetails/%s-%s-young" % (r, gender)] - own)

    # Where faces may change, from the eyes (the eyeballs' centres) and the head bone.
    e = np.array([v.co[:] for v in eyes.data.vertices], np.float64)
    me = np.array(eyes.matrix_world)
    e = e @ me[:3, :3].T + me[:3, 3]
    left, right = e[e[:, 0] > 0].mean(axis=0), e[e[:, 0] < 0].mean(axis=0)
    eye_z, eye_y = (left[2] + right[2]) / 2, (left[1] + right[1]) / 2
    x, y, z = world[:, 0], world[:, 1], world[:, 2]
    # Only the body's own vertices: the base mesh's helpers (tights, skirt, hair and joint helpers) are deleted later
    # and some of them move far with the race targets.
    skin = np.zeros(len(world), bool)
    group = body.vertex_groups["body"].index
    for v in body.data.vertices:
        for g in v.groups:
            if g.group == group and g.weight > 0.5:
                skin[v.index] = True

    def ramp(a, b, v):
        t = np.clip((v - a) / (b - a), 0.0, 1.0)
        return t * t * (3 - 2 * t)
    mask = (ramp(0.02, 0.034, np.linalg.norm(world - left, axis=1)) * ramp(0.02, 0.034, np.linalg.norm(world - right, axis=1))
            * ramp(eye_z + 0.02, eye_z + 0.004, z)          # brows and forehead
            * ramp(eye_z - 0.145, eye_z - 0.125, z)         # below the jaw
            * ramp(eye_y + 0.07, eye_y + 0.045, y)          # the back of the head
            * ramp(0.088, 0.072, np.abs(x))                 # the ears
            * skin)
    # The race targets also move the head as a whole (another neck, another height): only the face's own shape is
    # wanted, so the movement of the band round the eyes and brows is taken out.
    band = skin & (z > eye_z - 0.01) & (z < eye_z + 0.04) & (np.abs(x) < 0.07) & (y < eye_y + 0.03)
    for r in races:
        delta["race:" + r] = delta["race:" + r] - delta["race:" + r][band].mean(axis=0)
    faces = []
    for name, recipe in profile["faces"]:
        d = sum(w * delta[t] for t, w in recipe) * mask[:, None]
        moving = np.linalg.norm(d, axis=1) > 2e-5
        lo, hi = world[moving].min(axis=0) - 0.02, world[moving].max(axis=0) + 0.02
        near = np.all((world > lo) & (world < hi), axis=1) & skin
        far = int(np.argmax(np.linalg.norm(d, axis=1)))
        H.log("%s: %d vertices move, up to %.1f mm (at %s)" % (name, int(moving.sum()), np.linalg.norm(d, axis=1).max() * 1000,
                                                              np.round(world[far] - np.array((0, eye_y, eye_z)), 3)))
        faces.append((name, (world[near], d[near])))
    return faces


def blink_offsets(body, TargetService, LocationService):
    """The closed-eyelid shape of MakeHuman's expression units (both eyes), measured on a copy of the body while it
    still has the base mesh's vertex order: world positions and how far each moves (only the vertices that do)."""
    probe = H.duplicate(body, "blink_probe")
    units = os.path.join(LocationService.get_mpfb_data("targets"), "expression", "units", "caucasian")
    for side in ("left", "right"):
        TargetService.load_target(probe, os.path.join(units, "eye-%s-closure.target.gz" % side), weight=1.0,
                                  name="blink_" + side)
    keys = probe.data.shape_keys.key_blocks
    basis = np.array([v.co[:] for v in keys[0].data], np.float64)
    moved = np.zeros_like(basis)
    for kb in keys[1:]:
        moved += np.array([v.co[:] for v in kb.data], np.float64) - basis
    m = np.array(probe.matrix_world)
    world = basis @ m[:3, :3].T + m[:3, 3]
    shift = moved @ m[:3, :3].T
    moving = np.linalg.norm(shift, axis=1) > 1e-5
    # Keep the still vertices round the eyes too, so the movement can be interpolated (it fades to nothing).
    lo, hi = world[moving].min(axis=0) - 0.02, world[moving].max(axis=0) + 0.02
    near = np.all((world > lo) & (world < hi), axis=1)
    bpy.data.objects.remove(probe)
    H.log("blink: %d vertices move, up to %.1f mm" % (int(moving.sum()), np.linalg.norm(shift, axis=1).max() * 1000))
    return world[near], shift[near]


def shape_offsets(obj, shape):
    """World offsets of every vertex of `obj` for a shape measured on the base mesh (world positions, offsets): each
    vertex moves as the nearest measured vertices do (weighted by inverse distance), so the shape reaches decimated
    copies of the body and parts standing off it."""
    from mathutils.kdtree import KDTree
    world, shift = shape
    tree = KDTree(len(world))
    for i, p in enumerate(world):
        tree.insert(p, i)
    tree.balance()
    lo, hi = world.min(axis=0), world.max(axis=0)
    m = obj.matrix_world
    result = np.zeros((len(obj.data.vertices), 3), np.float32)
    for v in obj.data.vertices:
        p = m @ v.co
        if not (np.all(np.array(p[:]) > lo) and np.all(np.array(p[:]) < hi)):
            continue
        found = tree.find_n(p, 4)
        weights = [(1.0 / max(dist, 1e-4) ** 2, index) for _, index, dist in found if dist < 0.012]
        if not weights:
            continue
        total = sum(w for w, _ in weights)
        result[v.index] = sum((shift[i] * w for w, i in weights), np.zeros(3)) / total
    return result


def add_shape(obj, name, shape):
    """A shape key (blink, face_n) moving each vertex as the nearest measured vertices do (shape_offsets)."""
    offsets = shape_offsets(obj, shape)
    inv = obj.matrix_world.inverted().to_3x3()
    if obj.data.shape_keys is None:
        obj.shape_key_add(name="Basis", from_mix=False)
    key = obj.shape_key_add(name=name, from_mix=False)
    count = 0
    for v in obj.data.vertices:
        move = Vector(offsets[v.index].tolist())
        if move.length > 1e-5:
            key.data[v.index].co = v.co + inv @ move
            count += 1
    H.log("%s key on %s: %d vertices" % (name, obj.name, count))


def bbox_centre(obj):
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    return sum(corners, Vector()) / 8.0


def remove_helpers(body):
    """Deletes the helper geometry (tights, skirt, hair and joint helpers) that MakeHuman's base mesh carries."""
    group = body.vertex_groups["body"].index
    me = body.data
    keep = np.zeros(len(me.vertices), bool)
    for v in me.vertices:
        for g in v.groups:
            if g.group == group and g.weight > 0.5:
                keep[v.index] = True
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in range(len(keep)) if not keep[i]], context='VERTS')
    bm.to_mesh(me)
    bm.free()
    for m in list(body.modifiers):
        if m.type == 'MASK':
            body.modifiers.remove(m)
    H.log("body without helpers:", len(me.vertices), "vertices,", H.triangles(body), "triangles")


def clean(obj, rig):
    """Only the armature modifier, deform groups, smooth shading; materials are replaced later."""
    for m in list(obj.modifiers):
        if m.type != 'ARMATURE':
            obj.modifiers.remove(m)
    H.armature_modifier(obj, rig)
    H.remove_non_deform_groups(obj)
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.clear()


# ---------------------------------------------------------------- regions

ARM = {"upperarm": "upper_arm", "lowerarm": "forearm", "hand": "hand", "thumb": "hand", "index": "hand",
       "middle": "hand", "ring": "hand", "pinky": "hand"}


def landmarks(rig, eye_centre):
    b = {x.name: (rig.matrix_world @ x.head_local, rig.matrix_world @ x.tail_local) for x in rig.data.bones}
    hip = b["thigh_l"][0].z
    return {
        "waist": hip + 0.045,              # underwear waistband
        "leg": hip - 0.20,                 # underwear leg openings
        "chest": b["spine_03"][0].z + 0.06,
        "collar": (b["clavicle_l"][0].z + b["clavicle_r"][0].z) / 2 - 0.035,
        "boot": b["calf_l"][1].z + 0.16,   # top of a boot shaft
        "eye": eye_centre.z,
    }


def cut_underwear_edges(body, rig, eye_centre, extra=()):
    """Cuts the body along the waistband and leg openings, so the underwear's edges are straight lines rather than
    the zigzag of the mesh's faces. Arms are left alone (the hands hang beside the hips)."""
    marks = landmarks(rig, eye_centre)
    me = body.data
    names = {g.index: g.name for g in body.vertex_groups}
    arm = ("upperarm_", "lowerarm_", "hand_", "thumb_", "index_", "middle_", "ring_", "pinky_", "clavicle_")
    bm = bmesh.new()
    bm.from_mesh(me)
    deform = bm.verts.layers.deform.active
    before = len(bm.verts)
    for z in (marks["waist"], marks["leg"]) + tuple(extra):
        # The crop top's edges run over the upper chest, whose skin follows the collarbones: only the arm proper is
        # left alone there.
        limbs = arm if z in (marks["waist"], marks["leg"]) else arm[:-1]

        def on_arm(v, limbs=limbs):
            groups = v[deform]
            if not groups:
                return False
            g = max(groups.keys(), key=lambda k: groups[k])
            return names.get(g, "").startswith(limbs)
        faces = [f for f in bm.faces
                 if min(v.co.z for v in f.verts) <= z <= max(v.co.z for v in f.verts) and not any(on_arm(v) for v in f.verts)]
        edges = {e for f in faces for e in f.edges}
        verts = {v for f in faces for v in f.verts}
        bmesh.ops.bisect_plane(bm, geom=list(verts) + list(edges) + faces, dist=1e-6,
                               plane_co=(0.0, 0.0, z), plane_no=(0.0, 0.0, 1.0))
    unweighted = sum(1 for v in bm.verts if not v[deform])
    bm.to_mesh(me)
    bm.free()
    H.log("underwear edges cut:", len(me.vertices) - before, "new vertices,", unweighted, "without weights")


def classify(body, rig, eye_centre):
    """A region for every face, from its dominant bone and its height in the rest pose."""
    me = body.data
    weights = H.vertex_weights(body)
    marks = landmarks(rig, eye_centre)
    head_verts = [v.co for v in me.vertices if max(weights[v.index], key=weights[v.index].get, default="") == "head"]
    y_front = min(p.y for p in head_verts)
    y_back = max(p.y for p in head_verts)
    result = []
    fixed = set()
    for poly in me.polygons:
        sums = {}
        for vi in poly.vertices:
            for bone, w in weights[vi].items():
                sums[bone] = sums.get(bone, 0.0) + w
        bone = max(sums, key=sums.get) if sums else "pelvis"
        c = poly.center
        stem = bone.rsplit("_", 1)[0] if bone.endswith(("_l", "_r")) else bone
        stem = stem.split("_0")[0] if stem[:4] in ("thum", "inde", "midd", "ring", "pink") else stem
        if stem in ARM:
            region = ARM[stem]
        elif stem == "clavicle":
            region = "collar" if c.z > marks["collar"] else "chest"
        elif stem == "head":
            t = (c.y - y_front) / max(1e-6, y_back - y_front)
            line = marks["eye"] + 0.050 + (-0.095) * t
            region = "scalp" if c.z > line else "face"
        elif stem.startswith("neck"):
            region = "neck"
        elif stem in ("foot", "ball"):
            region = "foot"
        elif stem == "calf":
            region = "calf" if c.z > marks["boot"] else "ankle"
        elif c.z > marks["collar"] and abs(c.x) < 0.17:
            region = "collar"
        elif c.z > marks["chest"]:
            region = "chest"
        elif c.z > marks["waist"]:
            region = "belly"
        elif c.z > marks["leg"]:
            region = "pelvis"
        else:
            region = "thigh"
        if region in ("pelvis",) or (region in ("belly", "thigh") and abs(c.z - marks["waist"]) < 0.06) or                 (stem == "thigh" and abs(c.z - marks["leg"]) < 0.06):
            fixed.add(poly.index)
        result.append(H.REGION[region])
    return majority_filter(me, result, passes=2, fixed=fixed)


def majority_filter(me, regions, passes, fixed=frozenset()):
    """Removes specks: a face takes the region most of its edge neighbours share."""
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    for _ in range(passes):
        new = list(regions)
        for f in bm.faces:
            if f.index in fixed:
                continue
            counts = {}
            for e in f.edges:
                for g in e.link_faces:
                    if g.index != f.index:
                        counts[regions[g.index]] = counts.get(regions[g.index], 0) + 1
            if counts:
                best = max(counts, key=counts.get)
                if counts[best] >= 3 and best != regions[f.index]:
                    new[f.index] = best
        regions = new
    bm.free()
    return regions


def write_regions(obj, regions):
    attr = obj.data.attributes.get("region") or obj.data.attributes.new("region", 'INT', 'FACE')
    attr.data.foreach_set("value", regions)


def write_regions_from_attribute(obj):
    attr = obj.data.attributes["region"]
    values = [0] * len(obj.data.polygons)
    attr.data.foreach_get("value", values)
    H.set_region_uv(obj, values)


def bust_band(body, rig, eye_centre):
    """Heights of a crop top's lower and upper edges: under and above the bust's fullest point (the most forward
    point of the chest; the front is −Y)."""
    marks = landmarks(rig, eye_centre)
    torso = [v.co for v in body.data.vertices
             if abs(v.co.x) < 0.12 and marks["waist"] + 0.12 < v.co.z < marks["collar"]]
    bust = min(torso, key=lambda co: co.y).z
    return (bust - 0.08, bust + 0.1)


def crop_top(body, regions, band):
    """The faces of a woman's crop top: the trunk between the band's two heights, all round (whatever region the
    faces fell in there, except the arms)."""
    me = body.data
    arms = {H.REGION["upper_arm"], H.REGION["forearm"], H.REGION["hand"]}
    low, high = band
    faces = {p.index for p in me.polygons if regions[p.index] not in arms and low < p.center.z < high
             and abs(p.center.x) < 0.2}
    H.log("crop top: %d faces, %.2f to %.2f m" % (len(faces), low, high))
    return faces


def shape_underwear(body, regions, rig, top=frozenset()):
    """Smooths the pelvis region (no anatomy under the briefs) and any crop top, lifts them off the skin and adds a
    waistband."""
    me = body.data
    pelvis = H.REGION["pelvis"]
    marks_waist = landmarks(rig, Vector())["waist"]
    marks_leg = landmarks(rig, Vector())["leg"]
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    inside, edge = set(), set()
    for v in bm.verts:
        under = {regions[f.index] == pelvis or f.index in top for f in v.link_faces}
        if under == {True}:
            inside.add(v.index)
        elif True in under:
            edge.add(v.index)
    for _ in range(6):
        moved = {}
        for i in inside:
            v = bm.verts[i]
            ring = [e.other_vert(v).co for e in v.link_edges]
            avg = sum(ring, Vector()) / len(ring)
            moved[i] = v.co.lerp(avg, 0.5)
        for i, co in moved.items():
            bm.verts[i].co = co
    bm.normal_update()
    for v in bm.verts:
        if v.index not in inside and v.index not in edge:
            continue
        lift = 0.003 if v.index in inside else 0.0018
        if marks_waist - 0.03 < v.co.z:
            lift += 0.0016       # waistband
        if v.co.z < marks_leg + 0.012:
            lift += 0.0008       # leg hems
        v.co += v.normal * lift
    bm.to_mesh(me)
    bm.free()


def assign_body_materials(body, regions, top=frozenset(), suffix=""):
    me = body.data
    me.materials.append(material("skin" + suffix))
    me.materials.append(material("underwear"))
    pelvis = H.REGION["pelvis"]
    for poly in me.polygons:
        poly.material_index = 1 if regions[poly.index] == pelvis or poly.index in top else 0


def material(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)


def set_single_material(obj, name):
    obj.data.materials.clear()
    obj.data.materials.append(material(name))


# ---------------------------------------------------------------- textures

def look_textures(body, regions, eye, folder, stem, profile):
    """The textures a look needs besides the light skin:
    - <stem>_dark.png: the dark skin texture, its painted scalp hair replaced by its own skin tone (the hair base and
      the hair styles are drawn per player);
    - <stem>_mask.png: R the hair base painted on the scalp (it fades into the forehead with a broken, hair-like
      edge), G stubble over the jaw, chin and upper lip (not the lips), B the base's strand brightness."""
    import garment as G
    light, w, h = H.load_image_pixels(os.path.join(folder, stem + "_albedo.png"))
    light = np.flipud(light[..., :3]).copy()
    dark, _, _ = H.load_image_pixels(os.path.join(folder, stem + "_dark.png"))
    dark = np.flipud(dark[..., :3]).copy()
    covered, pos, _, face = G.rasterise(body, w)
    region = np.where(face >= 0, np.array(regions, np.int32)[np.clip(face, 0, None)], -1)
    head = {b.name: b for b in body.parent.data.bones}["head"]
    centre_y = (body.parent.matrix_world @ head.head_local).y
    x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]

    # The dark texture's painted hair: far darker than its own cheeks, on the scalp, the temples, round the ears and
    # at the nape (never round the eyes, the nostrils or the mouth).
    lum = dark @ np.array((0.3, 0.59, 0.11), np.float32)
    cheeks = covered & (region == H.REGION["face"]) & (z > eye.z - 0.08) & (z < eye.z - 0.03) & (y < centre_y - 0.06)
    reference = float(np.median(lum[cheeks]))
    from_eyes = np.minimum(np.hypot(np.abs(x) - 0.032, z - eye.z), 1.0)
    head_area = covered & np.isin(region, [H.REGION["face"], H.REGION["scalp"], H.REGION["neck"]])
    high = (z > eye.z - 0.04) | ((y > centre_y - 0.02) & (z > eye.z - 0.16))
    hair = head_area & high & (lum < 0.68 * reference) & (from_eyes > 0.03)
    hair |= covered & (region == H.REGION["scalp"])
    soft = skin_maps.blur(hair.astype(np.float32), 3)
    soft = np.clip(soft * 1.6, 0, 1)
    skin_dark = covered & (region == H.REGION["face"]) & ~hair & (z < eye.z - 0.02)
    ratio = dark[skin_dark].mean(axis=0) / np.maximum(light[skin_dark].mean(axis=0), 1e-3)
    dark = dark * (1 - soft[..., None]) + np.clip(light * ratio, 0, 1) * soft[..., None]
    H.save_image(S.to_blender_pixels(G.bleed(np.clip(dark, 0, 1), covered, 8)), w, h, os.path.join(folder, stem + "_dark.png"))
    H.log("dark skin: %d hair texels replaced (skin ratio %s)" % (int(hair.sum()), np.round(ratio, 3)))

    # The hair base: the hairline above the brows in front, round the temples, low at the nape (like the scalp
    # region's line).
    t = np.clip((y - (centre_y - 0.11)) / 0.2, 0, 1)
    line = eye.z + 0.062 - 0.095 * t
    strands = S.value_noise(w, 300, 51) * 0.6 + S.value_noise(w, 60, 52) * 0.4
    scalp = np.clip((z - line) / 0.022 + (strands - 0.5) * 0.9, 0, 1) * covered
    shade = S.value_noise(w, 120, 53)
    beard = np.zeros_like(scalp)
    if profile["stubble"]:
        front = np.clip((centre_y + 0.035 - y) / 0.05, 0, 1)
        jaw = np.clip(1 - np.abs(z - (eye.z - 0.095)) / 0.05, 0, 1) * front
        cheek_line = np.clip(((eye.z - 0.045) - z) / 0.02, 0, 1)
        lips = np.exp(-((x / 0.026) ** 2 + ((z - (eye.z - 0.083)) / 0.009) ** 2)) * (y < centre_y - 0.06)
        area = np.clip(jaw * cheek_line * 1.6, 0, 1) * (1 - np.clip(lips * 1.5, 0, 1)) * covered
        dots = (S.value_noise(w, 380, 41) > 0.52).astype(np.float32) * 0.6 + S.value_noise(w, 90, 42) * 0.4
        beard = area * (0.55 + 0.45 * dots)
    mask = np.stack([scalp, beard, shade], axis=-1).astype(np.float32)
    H.save_image(S.to_blender_pixels(G.bleed(mask, covered, 8)), w, h, os.path.join(folder, stem + "_mask.png"))
    H.log("look mask painted")


def hair_texture(path, gamma):
    """Turns a hair or brow texture into its brightness about its own mean (0.5 is the mean, in linear light), so the
    shader can colour it with any hair colour; alpha is kept."""
    px, w, h = H.load_image_pixels(path)
    rgb, alpha = px[..., :3], px[..., 3]
    linear = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    lum = linear @ np.array((0.2126, 0.7152, 0.0722), np.float32)
    weight = alpha > 0.5
    mean = float(lum[weight].mean()) if weight.any() else float(lum.mean())
    value = np.clip((np.maximum(lum, 1e-5) / max(mean, 1e-5)) ** gamma * 0.5, 0, 1)
    out = np.stack([value, value, value, alpha], axis=-1).astype(np.float32)
    H.save_image(out.ravel(), w, h, path)


def textures(out, profile=None):
    profile = profile or PROFILES["male"]
    _, _, LocationService = H.mpfb()
    data = LocationService.get_user_data()
    folder = os.path.join(out, "textures")
    os.makedirs(folder, exist_ok=True)
    suffix = profile["suffix"]
    which = "female" if suffix else "male"
    copies = {
        "skin_%s_albedo.png" % which: (profile["skin"], 2048),
        "skin_%s_dark.png" % which: (profile["skin_dark"], 2048),
        "eye_albedo.png": ("eyes/materials/brown_eye.png", 512),
        "brow%s_albedo.png" % suffix: (profile["brows"] + ".png", 512),
        "lash_albedo.png": ("eyelashes/eyelashes01/eyelashes01.png", 512),
    }
    for style in profile["hairs"]:
        mhmat = os.path.join(data, "hair", style, style + ".mhmat")
        with open(mhmat, encoding="utf-8") as f:
            diffuse = next(line.split()[1] for line in f if line.startswith("diffuseTexture"))
        copies["hair_%s_albedo.png" % style] = ("hair/%s/%s" % (style, diffuse), 1024)
        normal = diffuse.replace("_diffuse", "_normal")
        if normal != diffuse and os.path.exists(os.path.join(data, "hair", style, normal)):
            copies["hair_%s_normal.png" % style] = ("hair/%s/%s" % (style, normal), 1024)
    for name, (rel, size) in copies.items():
        H.resize_image(os.path.join(data, rel), os.path.join(folder, name), size)
        if name.startswith("hair_") and name.endswith("_albedo.png"):
            hair_texture(os.path.join(folder, name), 0.6)
        elif name.startswith("brow"):
            hair_texture(os.path.join(folder, name), 0.8)
    # Skin detail: fine pores and creases, tiled many times over the body.
    size = 512
    pores = S.value_noise(size, 96, 11, octaves=2) * 0.6 + S.value_noise(size, 24, 12, octaves=2) * 0.4
    dots = (S.value_noise(size, 160, 13) > 0.72).astype(np.float32) * 0.35
    height = pores - dots
    H.save_image(S.to_blender_pixels(S.height_to_normal(height, 2.2)), size, size, os.path.join(folder, "skin_detail_normal.png"))
    H.log("textures written to", os.path.relpath(folder, H.REPO))


if __name__ == "__main__":
    main()
