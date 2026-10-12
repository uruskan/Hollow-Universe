"""Builds the held items the character work tests with: game/assets/items/<id>.glb and their textures.

    blender -b --factory-startup --disable-autoexec --python art/items/build_items.py

Sources (GROUND.md §9): the PSG Starter Kit R02 (stone axe, stone pickaxe, rock, P250-style pistol) in
D:/GameDev/Assets/PSG_StarterKit_R02 and the PSG Frontier AK R05 in D:/GameDev/Projects/RustMVP/assets. Both are made for
this project by Blender Python, with CC0 Poly Haven textures. Their item names are replaced by ours:

    stone axe → flint axe · stone pickaxe → flint pick · rock → hand stone · P250 → Wren pistol · AK → Kestrel rifle

Every item keeps its authored frame (+X forward along the barrel or blade, +Z up, metres); the glTF export turns it into
Godot's (+X forward, +Y up). Sockets become empty nodes: socket_grip_r and socket_grip_l (palm centres),
socket_sight (the sight line), socket_eye (where the eye sits when aiming down the sights), socket_muzzle,
socket_impact. Moving parts of the pistol are their own nodes (part_slide, part_magazine, part_trigger, part_barrel).
Textures are copied at 2048 px at most; materials are placeholders named after the texture set.
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "lib"))

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import hollow_art as H  # noqa: E402

KIT = "D:/GameDev/Assets/PSG_StarterKit_R02"
FRONTIER = "D:/GameDev/Projects/RustMVP/assets/PSG_FrontierAK_R05"

ITEMS = {
    "flint_axe": {"blend": "StoneAxe", "mesh": "SM_StoneAxe", "sockets": {"grip_r": "HandR", "impact": "Impact"}},
    "flint_pick": {"blend": "StonePickaxe", "mesh": "SM_StonePickaxe", "sockets": {"grip_r": "HandR", "impact": "Impact"}},
    "hand_stone": {"blend": "Rock", "mesh": "SM_Rock", "scale": 0.62, "sockets": {"grip_r": "HandR", "impact": "Impact"}},
    "wren_pistol": {"blend": "P250", "mesh": "SM_P250_Body",
                    "parts": {"slide": "SM_P250_Slide", "barrel": "SM_P250_Barrel", "magazine": "SM_P250_Magazine",
                              "trigger": "SM_P250_Trigger"},
                    "sockets": {"grip_r": "HandR", "grip_l": "HandL", "sight": "Sight", "muzzle": "Muzzle"}},
}


def main():
    a = H.args()
    out = os.path.abspath(a.get("out", H.ITEMS))
    textures = os.path.join(out, "textures")
    os.makedirs(textures, exist_ok=True)
    for name, src in (("psg_surface_albedo.png", "T_PSG_Surface_BaseColor.png"), ("psg_surface_orm.png", "T_PSG_Surface_ORM.png"),
                      ("psg_surface_normal.png", "T_PSG_Surface_NormalGL.png")):
        H.resize_image(os.path.join(KIT, "Shared", "Textures", src), os.path.join(textures, name), 2048)
    for name, src in (("kestrel_metal_albedo.png", "T_FrontierAK_Metal_BaseColor.png"), ("kestrel_metal_orm.png", "T_FrontierAK_Metal_ORM.png"),
                      ("kestrel_metal_normal.png", "T_FrontierAK_Metal_NormalGL.png"),
                      ("kestrel_furniture_albedo.png", "T_FrontierAK_Furniture_BaseColor.png"),
                      ("kestrel_furniture_orm.png", "T_FrontierAK_Furniture_ORM.png"),
                      ("kestrel_furniture_normal.png", "T_FrontierAK_Furniture_NormalGL.png")):
        H.resize_image(os.path.join(FRONTIER, "Textures", src), os.path.join(textures, name), 2048)
    report = {}
    for item, spec in ITEMS.items():
        report[item] = kit_item(item, spec, out)
    report["kestrel_rifle"] = frontier_rifle(out)
    H.write_json(os.path.join(out, "items.json"), {
        "source": "PSG Starter Kit R02 and PSG Frontier AK R05 (made for this project; CC0 Poly Haven textures), "
                  "art/items/build_items.py",
        "items": report,
    })


def load_objects(path, names):
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n in names]
    objs = [o for o in dst.objects if o is not None]
    for o in objs:
        bpy.context.scene.collection.objects.link(o)
        o.parent = None
        o.hide_set(False)
        o.hide_render = False
    return {o.name: o for o in objs}


def clean_mesh(obj, material):
    obj.matrix_world = obj.matrix_world.copy()
    for m in list(obj.modifiers):
        obj.modifiers.remove(m)
    obj.data.materials.clear()
    obj.data.materials.append(bpy.data.materials.get(material) or bpy.data.materials.new(material))
    for attr in list(obj.data.color_attributes):
        obj.data.color_attributes.remove(attr)
    while len(obj.data.uv_layers) > 1:
        obj.data.uv_layers.remove(obj.data.uv_layers[-1])


def empty(name, location, parent):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.02
    bpy.context.scene.collection.objects.link(e)
    e.parent = parent
    e.location = location
    return e


def kit_item(item, spec, out):
    H.fresh_scene()
    folder = os.path.join(KIT, "Assets", spec["blend"])
    meta = json.load(open(os.path.join(folder, "asset.json"), encoding="utf-8"))
    names = [spec["mesh"], spec["mesh"] + "_LOD1", spec["mesh"] + "_LOD2"]
    for part in spec.get("parts", {}).values():
        names += [part, part + "_LOD1", part + "_LOD2"]
    objs = load_objects(os.path.join(folder, spec["blend"] + ".blend"), names)
    root = bpy.data.objects.new(item, None)
    bpy.context.scene.collection.objects.link(root)
    scale = spec.get("scale", 1.0)
    exported = [root]
    for level, suffix in enumerate(("", "_LOD1", "_LOD2")):
        body = objs[spec["mesh"] + suffix]
        clean_mesh(body, "psg_surface")
        body.name = body.data.name = "%s_lod%d" % (item, level)
        body.parent = root
        exported.append(body)
        for part, mesh in spec.get("parts", {}).items():
            p = objs[mesh + suffix]
            clean_mesh(p, "psg_surface")
            if level == 0:
                # The moving part: a node at its pivot, with every level of detail of the part under it.
                node = bpy.data.objects.get("part_" + part) or empty("part_" + part, p.matrix_world.translation.copy(), root)
                exported.append(node) if node not in exported else None
            node = bpy.data.objects["part_" + part]
            world = p.matrix_world.copy()
            p.parent = node
            p.matrix_world = world
            p.name = p.data.name = "%s_%s_lod%d" % (item, part, level)
            exported.append(p)
    for ours, theirs in spec["sockets"].items():
        loc = Vector(meta["sockets"][theirs]["location_m"])
        empty("socket_" + ours, loc, root)
    if "sight" in spec["sockets"]:
        sight = Vector(meta["sockets"][spec["sockets"]["sight"]]["location_m"])
        # A pistol is held at arm's length when aimed: the eye 30 cm behind the rear of the slide.
        empty("socket_eye", sight + Vector((-0.30, 0, 0)), root)
    root.scale = (scale, scale, scale)
    exported += [o for o in bpy.data.objects if o.name.startswith("socket_")]
    H.export_glb(os.path.join(out, item + ".glb"), exported)
    return {"source": "PSG_StarterKit_R02/" + spec["blend"], "triangles": [H.triangles(o) for o in exported
                                                                          if o.type == 'MESH' and o.name.startswith(item + "_lod")]}


def frontier_rifle(out):
    H.fresh_scene()
    item = "kestrel_rifle"
    blend = os.path.join(FRONTIER, "Source", "FrontierAK_R05_Game.blend")
    names = ["SM_FrontierAK_R05_LOD1", "SM_FrontierAK_R05_LOD2", "SM_FrontierAK_R05_LOD3", "CAM_ADS"]
    objs = load_objects(blend, names)
    root = bpy.data.objects.new(item, None)
    bpy.context.scene.collection.objects.link(root)
    exported = [root]
    for level, name in enumerate(names[:3]):
        o = objs[name]
        for m in list(o.modifiers):
            o.modifiers.remove(m)
        o.vertex_groups.clear()
        for slot in o.material_slots:
            if slot.material and "Metal" in slot.material.name:
                slot.material = bpy.data.materials.get("kestrel_metal") or bpy.data.materials.new("kestrel_metal")
            elif slot.material:
                slot.material = bpy.data.materials.get("kestrel_furniture") or bpy.data.materials.new("kestrel_furniture")
        for attr in list(o.data.color_attributes):
            o.data.color_attributes.remove(attr)
        while len(o.data.uv_layers) > 1:
            o.data.uv_layers.remove(o.data.uv_layers[-1])
        o.parent = root
        o.name = o.data.name = "%s_lod%d" % (item, level)
        exported.append(o)
    contract = json.load(open(os.path.join(FRONTIER, "Exports", "socket_contract.json"), encoding="utf-8"))
    by_name = {s["name"]: s for s in contract["sockets"]}
    for ours, theirs in (("grip_r", "socket_grip_r"), ("grip_l", "socket_grip_l"), ("sight", "socket_ads"),
                         ("muzzle", "socket_muzzle")):
        exported.append(empty("socket_" + ours, Vector(by_name[theirs]["position_blender_m"]), root))
    # The kit's own aiming camera is where the eye goes.
    exported.append(empty("socket_eye", objs["CAM_ADS"].matrix_world.translation.copy(), root))
    bpy.data.objects.remove(objs["CAM_ADS"])
    H.export_glb(os.path.join(out, item + ".glb"), exported)
    return {"source": "PSG_FrontierAK_R05 (SM LOD1–LOD3)", "triangles": [H.triangles(o) for o in exported if o.type == 'MESH']}


if __name__ == "__main__":
    main()
