"""Shared helpers for Hollow Universe's Blender build scripts.

Every script in art/ runs in Blender 5.2 in background mode, e.g.

    blender -b --factory-startup --python art/characters/build_body.py -- --out game/assets/characters

The MakeHuman-based scripts need MPFB 2 installed in a separate Blender user folder, named by the environment variable
BLENDER_USER_RESOURCES (see AGENTS.md and GROUND.md section 9).
"""
import json
import math
import os
import sys
import time

import bmesh
import bpy
from mathutils import Matrix, Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CACHE = os.path.join(REPO, "art", ".cache")
CHARACTERS = os.path.join(REPO, "game", "assets", "characters")
ITEMS = os.path.join(REPO, "game", "assets", "items")

_T0 = time.time()

# Body regions, in this order. The client hides a region by setting its bit in a mask (CharacterRegions.cs uses
# the same numbers). Each vertex stores its region in the second UV map ("region"), as u = index + 0.5.
REGIONS = [
    "face",       # 0  face, ears and the front of the neck below the jaw line
    "scalp",      # 1  where a cap sits: crown, back of the head
    "neck",       # 2
    "chest",      # 3  chest, shoulders, upper back
    "belly",      # 4  belly, lower back, down to the waist
    "pelvis",     # 5  the underwear
    "upper_arm",  # 6
    "forearm",    # 7
    "hand",       # 8
    "thigh",      # 9
    "calf",       # 10 knee to boot top
    "ankle",      # 11 boot shaft
    "foot",       # 12
]
REGION = {name: i for i, name in enumerate(REGIONS)}


def log(*parts):
    print("[hollow %6.1fs]" % (time.time() - _T0), *parts, flush=True)


def args():
    """Command-line options after '--' as a dict: --key value, or --flag (True)."""
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    result = {}
    i = 0
    while i < len(argv):
        key = argv[i]
        if key.startswith("--"):
            key = key[2:]
            if i + 1 < len(argv) and not argv[i + 1].startswith("--"):
                result[key] = argv[i + 1]
                i += 2
                continue
            result[key] = True
        i += 1
    return result


def fresh_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.render.fps = 30
    return scene


# ---------------------------------------------------------------- MPFB

def enable_mpfb():
    import addon_utils
    if not os.environ.get("BLENDER_USER_RESOURCES"):
        log("warning: BLENDER_USER_RESOURCES is not set; MPFB is looked for in Blender's default user folder")
    module = addon_utils.enable("bl_ext.user_default.mpfb", default_set=True, persistent=False)
    if module is None:
        raise SystemExit("MPFB 2 is not installed in this Blender user folder (GROUND.md section 9 says how).")
    return module


def mpfb():
    """MPFB's services, imported after enable_mpfb()."""
    from bl_ext.user_default.mpfb.services.humanservice import HumanService
    from bl_ext.user_default.mpfb.services.targetservice import TargetService
    from bl_ext.user_default.mpfb.services.locationservice import LocationService
    return HumanService, TargetService, LocationService


# The adult male: MakeHuman macro values (0..1). Age 0.5 is 25 years; MakeHuman's own default race mix is 1/3 each.
MALE = {
    "gender": 1.0, "age": 0.58, "muscle": 0.72, "weight": 0.6, "height": 0.56, "proportions": 0.6,
    "cupsize": 0.5, "firmness": 0.5,
    "race": {"caucasian": 0.62, "african": 0.19, "asian": 0.19},
}


def make_male():
    """The MakeHuman male with MPFB's game-engine skeleton (UE-style bone names, 53 bones) and its weights."""
    HumanService, TargetService, _ = mpfb()
    macro = TargetService.get_default_macro_info_dict()
    macro.update({k: v for k, v in MALE.items() if k != "race"})
    macro["race"] = dict(MALE["race"])
    body = HumanService.create_human(mask_helpers=True, detailed_helpers=True, extra_vertex_groups=True,
                                     feet_on_ground=True, scale=0.1, macro_detail_dict=macro)
    rig = HumanService.add_builtin_rig(body, "game_engine", import_weights=True)
    return body, rig


# ---------------------------------------------------------------- objects and modifiers

def select_only(objects, active=None):
    bpy.context.view_layer.update()
    for o in bpy.data.objects:
        if o is not None and o.name in bpy.context.view_layer.objects:
            o.select_set(False)
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or (objects[0] if objects else None)


def apply_modifier(obj, name, first=True):
    """Applies one modifier (moved to the top of the stack first, so it applies to the original mesh)."""
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj], selected_editable_objects=[obj]):
        if first:
            bpy.ops.object.modifier_move_to_index(modifier=name, index=0)
        bpy.ops.object.modifier_apply(modifier=name)


def duplicate(obj, name):
    copy = obj.copy()
    copy.data = obj.data.copy()
    copy.name = name
    copy.data.name = name
    for c in obj.users_collection:
        c.objects.link(copy)
    return copy


def decimate(obj, ratio):
    mod = obj.modifiers.new("lod", 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = ratio
    mod.use_symmetry = True
    mod.symmetry_axis = 'X'
    mod.use_collapse_triangulate = False
    apply_modifier(obj, mod.name)


def triangles(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def armature_modifier(obj, rig):
    for m in obj.modifiers:
        if m.type == 'ARMATURE':
            m.object = rig
            return m
    m = obj.modifiers.new("Armature", 'ARMATURE')
    m.object = rig
    return m


def parent_to_rig(obj, rig):
    world = obj.matrix_world.copy()
    obj.parent = rig
    obj.parent_type = 'OBJECT'
    obj.matrix_world = world
    armature_modifier(obj, rig)


# ---------------------------------------------------------------- weights

DEFORM_PREFIXES = ("pelvis", "spine_", "neck_", "head", "clavicle_", "upperarm_", "lowerarm_", "hand_", "thumb_",
                   "index_", "middle_", "ring_", "pinky_", "thigh_", "calf_", "foot_", "ball_")


def is_deform_group(name):
    return name.startswith(DEFORM_PREFIXES)


def vertex_weights(obj, normalise=True):
    """Per vertex: {bone: weight} for deform groups."""
    names = {g.index: g.name for g in obj.vertex_groups if is_deform_group(g.name)}
    result = []
    for v in obj.data.vertices:
        w = {}
        for g in v.groups:
            if g.group in names and g.weight > 1e-4:
                w[names[g.group]] = w.get(names[g.group], 0.0) + g.weight
        if normalise:
            total = sum(w.values())
            if total > 0:
                w = {k: x / total for k, x in w.items()}
        result.append(w)
    return result


def limit_and_normalise_weights(obj, limit=4):
    """At most four influences per vertex (what the client skins with), normalised; other groups removed."""
    groups = {g.index: g for g in obj.vertex_groups}
    for v in obj.data.vertices:
        entries = [(g.group, g.weight) for g in v.groups if g.group in groups and is_deform_group(groups[g.group].name)]
        entries.sort(key=lambda e: -e[1])
        keep = entries[:limit]
        total = sum(w for _, w in keep) or 1.0
        for gi, _ in entries[limit:]:
            groups[gi].remove([v.index])
        for gi, w in keep:
            groups[gi].add([v.index], w / total, 'REPLACE')


def remove_non_deform_groups(obj):
    for g in list(obj.vertex_groups):
        if not is_deform_group(g.name):
            obj.vertex_groups.remove(g)


# ---------------------------------------------------------------- regions

def set_region_uv(obj, face_regions):
    """Writes each face's region into the second UV map, so every triangle's corners share one region."""
    me = obj.data
    while len(me.uv_layers) > 1:
        me.uv_layers.remove(me.uv_layers[-1])
    if len(me.uv_layers) == 0:
        me.uv_layers.new(name="UVMap")
    layer = me.uv_layers.new(name="region")
    data = layer.data
    for poly in me.polygons:
        u = face_regions[poly.index] + 0.5
        for li in poly.loop_indices:
            data[li].uv = (u, 0.5)
    me.uv_layers.active_index = 0
    for i, uv in enumerate(me.uv_layers):
        uv.active_render = (i == 0)


# ---------------------------------------------------------------- images

def save_image(pixels, width, height, path, alpha=True):
    """Saves a flat RGBA float list/array (rows bottom to top, as Blender stores them) as an 8-bit PNG."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    name = os.path.basename(path)
    img = bpy.data.images.new(name, width, height, alpha=alpha, float_buffer=False)
    img.colorspace_settings.name = 'Non-Color'
    img.pixels.foreach_set(pixels)
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def load_image_pixels(path):
    import numpy as np
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = 'Non-Color'
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4), w, h


def resize_image(path_in, path_out, size):
    """Copies an image at most `size` pixels on a side (Blender's own scaler), as PNG."""
    img = bpy.data.images.load(path_in, check_existing=False)
    img.colorspace_settings.name = 'Non-Color'
    w, h = img.size
    if max(w, h) > size:
        f = size / max(w, h)
        img.scale(max(1, int(round(w * f))), max(1, int(round(h * f))))
    os.makedirs(os.path.dirname(path_out), exist_ok=True)
    img.filepath_raw = path_out
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


# ---------------------------------------------------------------- export

def export_glb(path, objects, *, animations=False, active_actions=None):
    """Exports the given objects (an armature and its meshes) as GLB. Materials are placeholders: the client builds
    its own from the textures next to the file. Modifiers other than the armature are applied."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    select_only(objects)
    kwargs = dict(
        filepath=path, export_format='GLB', use_selection=True, export_apply=True, export_yup=True,
        export_texcoords=True, export_normals=True, export_tangents=False, export_materials='EXPORT', export_image_format='NONE',
        export_vertex_color='NONE', export_attributes=False, export_skins=True, export_influence_nb=4,
        export_morph=False, export_animations=animations, export_def_bones=False, export_leaf_bone=False,
        export_rest_position_armature=True, export_extras=False, export_cameras=False, export_lights=False,
    )
    if animations:
        kwargs.update(export_animation_mode='ACTIONS', export_force_sampling=True, export_frame_step=1,
                      export_optimize_animation_size=False, export_anim_single_armature=True,
                      export_reset_pose_bones=True, export_anim_slide_to_zero=True, export_bake_animation=False)
    bpy.ops.export_scene.gltf(**kwargs)
    log("exported", os.path.relpath(path, REPO), "%.1f MB" % (os.path.getsize(path) / 1e6))


def write_json(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2, sort_keys=False)
        f.write("\n")
    log("wrote", os.path.relpath(path, REPO))
