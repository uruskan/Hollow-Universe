"""Clothing items made from the garments players wear (docs/ITEM_ART_CLOTHING.md): the woven hood, shirt, trousers and
gloves, the work boots and the chest plate. Each item is the character package's own garment mesh at its three LODs
(game/assets/characters/clothing.glb, built by art/characters/build_clothing.py), so the inventory icon and the
dropped item show exactly what is worn. Their material is the garment's: the character cloth shader with the
garment's albedo, normal and ORM textures and its tiled weave (CharacterMaterials.Garment), written as a shared
game/assets/materials/garment-<stem>.tres.

The male garments are used: clothing.glb is the default body's, and the female garments are fitted versions of the
same pieces. Garments keep their bind pose, upright with the front toward -Y (Godot +Z), as the other clothing items
lie; the gloves, which hang fingers-down at the A-pose's hands, are turned fingers-up with their backs to the front
and set side by side, and the boots are brought closer together. Bottom pickup pivot, convex collision.

    blender -b --factory-startup --python art/models/garment_items.py -- [keys...]
"""
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from hollow import ASSETS, ITEMS, MATERIALS, REPO, script_args  # noqa: E402
from hollow.export import collection, convex_hull, copy_object, export_glb, keep_uv  # noqa: E402

CHARACTERS = ASSETS / "characters"
TEXTURES = "res://assets/characters/textures/"
OUT = REPO / ".artifacts/itemart/garments"

# Item key: (garment mesh stem in clothing.glb, node name stem, how the pieces are arranged).
GARMENTS = {
    "woven-hood": ("woven_hood", "WovenHood", None),
    "woven-shirt": ("woven_shirt", "WovenShirt", None),
    "woven-trousers": ("woven_trousers", "WovenTrousers", None),
    "woven-gloves": ("woven_gloves", "WovenGloves", "gloves"),
    "work-boots": ("laced_boots", "WorkBoots", "boots"),
    "chest-plate": ("riveted_breastplate", "ChestPlate", None),
}


def material(stem, surfaces):
    """The garment's material as CharacterMaterials.Garment builds it, written as a shared .tres."""
    spec = surfaces.get(stem, {})
    textures = {"albedo_texture": f"{stem}_albedo.png", "normal_texture": f"{stem}_normal.png",
                "orm_texture": f"{stem}_orm.png", "detail_normal": f"{spec.get('detail', 'weave')}_normal.png"}
    textures = {k: v for k, v in textures.items() if (CHARACTERS / "textures" / v).exists()}
    lines = [f'[gd_resource type="ShaderMaterial" load_steps={len(textures) + 2} format=3]', "",
             '[ext_resource type="Shader" path="res://shaders/character_cloth.gdshader" id="1"]']
    ids = {}
    for i, (param, file) in enumerate(textures.items(), start=2):
        lines.append(f'[ext_resource type="Texture2D" path="{TEXTURES}{file}" id="{i}"]')
        ids[param] = i
    params = {"tint": "Color(1, 1, 1, 1)", "roughness": spec.get("roughness", 0.9),
              "detail_tiling": spec.get("tiling", 40.0), "detail_strength": spec.get("detail_strength", 0.6),
              "metallic": spec.get("metallic", 0.0), "specular_level": spec.get("specular", 0.35),
              "normal_strength": spec.get("normal_strength", 1.0)}
    if "detail_normal" not in ids:
        params["detail_strength"] = 0.0
    if "orm_texture" in ids:
        params["use_orm"] = "true"
    lines += ["", "[resource]", f'resource_name = "M_Hollow_Garment_{stem}"', 'shader = ExtResource("1")']
    for param, i in ids.items():
        lines.append(f'shader_parameter/{param} = ExtResource("{i}")')
    for param, value in params.items():
        lines.append(f"shader_parameter/{param} = {value if isinstance(value, str) else round(float(value), 4)}")
    path = MATERIALS / f"garment-{stem.replace('_', '-')}.tres"
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    return path


def side_transforms(lod0, arrange):
    """For gloves and boots, one transform per side (x < 0, x >= 0) measured on LOD0 and applied to every LOD."""
    co = np.array([v.co[:] for v in lod0.data.vertices])
    out = {}
    for side in (-1, 1):
        pts = co[(co[:, 0] < 0) if side < 0 else (co[:, 0] >= 0)]
        centre = pts.mean(axis=0)
        if arrange == "boots":
            # Keep each boot as it stands; close the gap between them to about 4 cm.
            inner = pts[:, 0].max() if side < 0 else pts[:, 0].min()
            out[side] = Matrix.Translation((-side * (abs(inner) - 0.02), 0, 0))
            continue
        # A glove: its longest axis runs wrist to fingertips, its thinnest is the palm's normal.
        values, vectors = np.linalg.eigh(np.cov((pts - centre).T))
        length, palm = Vector(vectors[:, 2]), Vector(vectors[:, 0])
        tips = pts[np.argmax((pts - centre) @ np.array(length))]
        wrist = pts[np.argmin((pts - centre) @ np.array(length))]
        if tips[2] > wrist[2]:  # the end lower down in the A-pose is the fingertips
            length = -length
        if palm.dot(Vector((side, 0, 0))) > 0:  # the palm faces the thigh, inward: its back faces outward
            palm = -palm
        # Fingers up (+Z) and the palm away from the viewer (+Y), so the back of the hand faces the front (-Y); a
        # right-handed frame (across, palm, length) is turned onto (X, Y, Z), then each glove leans a little outward.
        across = palm.cross(length).normalized()
        palm = length.cross(across).normalized()
        upright = Matrix((across, palm, length))  # rows: this rotation takes the frame onto the axes
        lean = Matrix.Rotation(math.radians(12 * side), 3, "Y")
        out[side] = (Matrix.Translation((side * 0.075, 0, 0)) @ (lean @ upright).to_4x4()
                     @ Matrix.Translation(-Vector(centre)))
    return out


def build(key, garments, surfaces, col):
    stem, name, arrange = GARMENTS[key]
    tres = material(stem, surfaces)
    lods = []
    for lod in range(3):
        o = copy_object(garments[f"{stem}_lod{lod}"], f"{name}_lod{lod}", col)
        o.vertex_groups.clear()
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
        keep_uv(o, "UVMap")
        lods.append(o)
    if arrange:
        moves = side_transforms(lods[0], arrange)
        for o in lods:
            for v in o.data.vertices:
                v.co = moves[-1 if v.co.x < 0 else 1] @ v.co
            o.data.update()
    co = np.array([v.co[:] for v in lods[0].data.vertices])
    offset = Vector(((co[:, 0].min() + co[:, 0].max()) / -2, (co[:, 1].min() + co[:, 1].max()) / -2, -co[:, 2].min()))
    for o in lods:
        o.data.transform(Matrix.Translation(offset))
        o.data.update()
    hull = convex_hull([lods[0]], f"{name}_hull0-convcolonly", col, max_faces=40)
    for o in lods:
        if not o.data.materials or o.data.materials[0] is None:
            raise RuntimeError(f"{o.name}: no material")
        o.data.materials[0].name = stem
    export_glb(ITEMS / f"{key}.glb", lods + [hull], {stem: tres})
    co = np.array([v.co[:] for v in lods[0].data.vertices])
    report = {"asset": key, "garment": stem, "dimensions_blender_xyz_m": (co.max(0) - co.min(0)).tolist(),
              "material": tres.relative_to(REPO).as_posix(), "material_count": 1,
              "lod_triangles": {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in lods},
              "hull_triangles": sum(len(p.vertices) - 2 for p in hull.data.polygons)}
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / f"geometry-{key}.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report), flush=True)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(CHARACTERS / "clothing.glb"))
garments = {o.name: o for o in bpy.data.objects if o.type == "MESH"}
surfaces = json.loads((CHARACTERS / "clothing.json").read_text(encoding="utf-8"))["surfaces"]
for key in script_args() or list(GARMENTS):
    build(key, garments, surfaces, collection(f"Garment_{key}"))
