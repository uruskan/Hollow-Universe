"""Builds the player body: game/assets/characters/human_male.glb, its textures and human_male.json.

    set BLENDER_USER_RESOURCES=D:\\GameDev\\Tools\\blender-hollow-config
    blender -b --factory-startup --python art/characters/build_body.py

What it does:
1. MakeHuman male from MPFB 2 (macro values in hollow_art.MALE), MPFB's "game_engine" skeleton (53 bones, UE-style
   names) and its weights. Rest pose: MakeHuman's A-pose. The animation script retargets every clip onto this rig.
2. Eyes, eyebrows, eyelashes and short hair from the MakeHuman CC0 system assets, fitted and weighted by MPFB.
3. Every body face gets a region (hollow_art.REGIONS); clothing hides regions it covers, so nothing pokes through.
4. Underwear is part of the body: the pelvis region, smoothed, lifted 3 mm and given its own fabric surface.
5. Levels of detail by collapse decimation: about 18k / 7k / 2.4k triangles for the body.
6. Saves art/.cache/human_male.blend for the clothing and animation scripts.
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

# For each part: (target triangles or None for full detail, levels of detail it is drawn at).
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
    "hair": ("Hair", "hair/short02/short02.mhclo"),
}
MATERIAL_OF = {"eyes": "eye", "brows": "brow", "lashes": "lash", "hair": "hair"}


def main():
    a = H.args()
    out = os.path.abspath(a.get("out", H.CHARACTERS))
    H.fresh_scene()
    H.enable_mpfb()
    HumanService, TargetService, LocationService = H.mpfb()

    body, rig = H.make_male()
    rig.name = rig.data.name = "Rig"
    body.name = body.data.name = "body"
    parts = {}
    for key, (kind, rel) in PARTS.items():
        path = os.path.join(LocationService.get_user_data(), rel)
        obj = HumanService.add_mhclo_asset(path, body, asset_type=kind, subdiv_levels=0, material_type="MAKESKIN")
        obj.name = obj.data.name = key
        parts[key] = obj
    H.log("human built:", {k: H.triangles(o) for k, o in parts.items()})

    TargetService.bake_targets(body)
    eye_centre = bbox_centre(parts["eyes"])
    remove_helpers(body)
    for obj in [body] + list(parts.values()):
        clean(obj, rig)

    cut_underwear_edges(body, rig, eye_centre)
    regions = classify(body, rig, eye_centre)
    write_regions(body, regions)
    shape_underwear(body, regions, rig)
    assign_body_materials(body, regions)
    for key, obj in parts.items():
        set_single_material(obj, MATERIAL_OF[key])
        H.set_region_uv(obj, [H.REGION["face"]] * len(obj.data.polygons))

    textures(out)
    stubble(body, eye_centre, os.path.join(out, "textures", "skin_male_albedo.png"))
    skin_maps.paint(body, os.path.join(out, "textures"))

    # Levels of detail.
    lods, levels = {}, {}
    for key, obj in [("body", body)] + list(parts.items()):
        for target, drawn in LODS[key]:
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

    # The cache keeps the full-resolution body with its regions, for the clothing script.
    body.name = "body_source"
    body.hide_render = True
    os.makedirs(H.CACHE, exist_ok=True)
    cache = os.path.join(H.CACHE, "human_male.blend")
    bpy.ops.wm.save_as_mainfile(filepath=cache, check_existing=False, compress=True)
    H.log("saved", os.path.relpath(cache, H.REPO))

    H.export_glb(os.path.join(out, "human_male.glb"), [rig] + list(lods.values()))
    bones = {b.name: [round(x, 4) for x in (rig.matrix_world @ b.head_local)] for b in rig.data.bones}
    H.write_json(os.path.join(out, "human_male.json"), {
        "source": "MakeHuman (MPFB 2.0.17, CC0 base mesh and system assets), art/characters/build_body.py",
        "height_m": round(body.dimensions.z, 3),
        "eye_height_m": round(eye_centre.z, 3),
        "regions": H.REGIONS,
        "meshes": {name: {"lods": levels[name], "triangles": counts[name]} for name in lods},
        "bones": len(rig.data.bones),
        "bone_heads_blender": bones,
    })
    if a.get("preview"):
        import preview
        preview.render(a["preview"], [rig] + [o for n, o in lods.items() if n.endswith("lod0")], out)


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


def cut_underwear_edges(body, rig, eye_centre):
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
    for z in (marks["waist"], marks["leg"]):
        def on_arm(v):
            groups = v[deform]
            if not groups:
                return False
            g = max(groups.keys(), key=lambda k: groups[k])
            return names.get(g, "").startswith(arm)
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


def shape_underwear(body, regions, rig):
    """Smooths the pelvis region (no anatomy under the briefs), lifts it off the skin and adds a waistband."""
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
        rs = {regions[f.index] for f in v.link_faces}
        if rs == {pelvis}:
            inside.add(v.index)
        elif pelvis in rs:
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


def assign_body_materials(body, regions):
    me = body.data
    me.materials.append(material("skin"))
    me.materials.append(material("underwear"))
    pelvis = H.REGION["pelvis"]
    for poly in me.polygons:
        poly.material_index = 1 if regions[poly.index] == pelvis else 0


def material(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)


def set_single_material(obj, name):
    obj.data.materials.clear()
    obj.data.materials.append(material(name))


# ---------------------------------------------------------------- textures

def stubble(body, eye, path):
    """Paints a few days' stubble over the jaw, chin and upper lip of the skin texture (not on the lips)."""
    import garment as G
    px, w, h = H.load_image_pixels(path)
    img = np.flipud(px[..., :3]).copy()
    mask, pos, _, _ = G.rasterise(body, w)
    head = {b.name: b for b in body.parent.data.bones}["head"]
    centre_y = (body.parent.matrix_world @ head.head_local).y
    x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]
    front = np.clip((centre_y + 0.035 - y) / 0.05, 0, 1)
    jaw = np.clip(1 - np.abs(z - (eye.z - 0.095)) / 0.05, 0, 1) * front
    cheek_line = np.clip(((eye.z - 0.045) - z) / 0.02, 0, 1)
    lips = np.exp(-((x / 0.026) ** 2 + ((z - (eye.z - 0.083)) / 0.009) ** 2)) * (y < centre_y - 0.06)
    area = np.clip(jaw * cheek_line * 1.6, 0, 1) * (1 - np.clip(lips * 1.5, 0, 1)) * mask
    dots = (S.value_noise(w, 380, 41) > 0.52).astype(np.float32) * 0.6 + S.value_noise(w, 90, 42) * 0.4
    shade = 1 - 0.42 * area * (0.55 + 0.45 * dots)
    tint = np.array((0.95, 0.97, 1.0), np.float32)
    img = img * (1 - (1 - shade[..., None]) * tint)
    H.save_image(S.to_blender_pixels(np.clip(img, 0, 1)), w, h, path)
    H.log("stubble painted")



def textures(out):
    _, _, LocationService = H.mpfb()
    data = LocationService.get_user_data()
    folder = os.path.join(out, "textures")
    os.makedirs(folder, exist_ok=True)
    copies = {
        "skin_male_albedo.png": ("skins/young_caucasian_male/young_lightskinned_male_diffuse.png", 2048),
        "eye_albedo.png": ("eyes/materials/brown_eye.png", 512),
        "brow_albedo.png": ("eyebrows/eyebrow001/eyebrow001.png", 512),
        "lash_albedo.png": ("eyelashes/eyelashes01/eyelashes01.png", 512),
        "hair_albedo.png": ("hair/short02/short02_diffuse.png", 1024),
        "hair_normal.png": ("hair/short02/short02_normal.png", 1024),
    }
    for name, (rel, size) in copies.items():
        H.resize_image(os.path.join(data, rel), os.path.join(folder, name), size)
    # Skin detail: fine pores and creases, tiled many times over the body.
    size = 512
    pores = S.value_noise(size, 96, 11, octaves=2) * 0.6 + S.value_noise(size, 24, 12, octaves=2) * 0.4
    dots = (S.value_noise(size, 160, 13) > 0.72).astype(np.float32) * 0.35
    height = pores - dots
    H.save_image(S.to_blender_pixels(S.height_to_normal(height, 2.2)), size, size, os.path.join(folder, "skin_detail_normal.png"))
    H.log("textures written to", os.path.relpath(folder, H.REPO))


if __name__ == "__main__":
    main()
