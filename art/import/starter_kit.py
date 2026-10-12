"""PSG Starter Kit R02 -> game/assets/items and game/assets/world (GROUND.md §9).

Reads each asset's .blend (metres, +X forward, +Z up): the game meshes (LOD0), the LOD1/LOD2 references, the UCX
convex hulls and the socket empties. Writes one GLB per item with Hollow's names, axes and pivots, and the shared
atlas (downscaled to 2K) with its Godot materials. The kit's AK-style rifle is not used: the Frontier rifle replaces
it (art/import/frontier_rifle.py). The solar panel isn't an item yet.

    blender -b --factory-startup --python art/import/starter_kit.py -- [kit asset names...]
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from hollow import ITEMS, SOURCES, TEXTURES, WORLD, log, script_args  # noqa: E402
from hollow.godot import write_material  # noqa: E402
from hollow.kit import TURN_HELD, TURN_WORLD, Conversion, objects_in, snake, world_bounds  # noqa: E402
from hollow.textures import convert, write_texture_import  # noqa: E402

KIT = SOURCES / "PSG_StarterKit_R02"
ATLAS = TEXTURES / "psg"

# Kit asset -> (item key, output folder, role, scale, pivot). Roles: "held" points Blender +Y with the pivot at a
# socket (the right hand's grip); "mounted" faces Blender -Y with the pivot at the socket that touches the door or
# wall; "placed" faces Blender -Y with the kit's own ground-centre pivot.
ASSETS = {
    "P250": ("pistol", ITEMS, "held", 1.0, "HandR"),
    "Rock": ("rock", ITEMS, "held", 0.4, "centre"),
    "StoneAxe": ("stone-hatchet", ITEMS, "held", 1.0, "HandR"),
    "StonePickaxe": ("stone-pickaxe", ITEMS, "held", 1.0, "HandR"),
    "BuildingHammer": ("hammer", ITEMS, "held", 1.0, "HandR"),
    "BuildingPlan": ("building-plan", ITEMS, "held", 1.0, "HandR"),
    "C4": ("timed-charge", ITEMS, "mounted", 1.0, "Attach"),
    "CodeLock": ("code-lock", ITEMS, "mounted", 1.0, "Mount"),
    "SleepingBag": ("sleeping-bag", WORLD, "placed", 1.0, None),
    "LargeStorageBox": ("large-box", WORLD, "placed", 1.0, None),
    "ToolCupboard": ("claim-device", WORLD, "placed", 1.0, None),
    "SheetMetalDoor": ("metal-door", WORLD, "placed", 1.0, None),
    "Workbench_T1": ("workbench-1", WORLD, "placed", 1.0, None),
    "Workbench_T2": ("workbench-2", WORLD, "placed", 1.0, None),
    "Workbench_T3": ("workbench-3", WORLD, "placed", 1.0, None),
    "VehicleWorkbench": ("vehicle-workbench", WORLD, "placed", 1.0, None),
    "EngineeringWorkbench": ("engineering-workbench", WORLD, "placed", 1.0, None),
}


def atlas_materials():
    """The kit's shared atlas at 2K and its two Godot materials; returns {Blender material name: .tres}."""
    src = KIT / "Shared" / "Textures"
    maps = {}
    for name, normal in (("BaseColor", False), ("ORM", False), ("NormalGL", True), ("Emissive", False)):
        target = ATLAS / f"T_PSG_Surface_{name}.png"
        if not target.exists():
            convert(src / f"T_PSG_Surface_{name}.png", target, 2048, normal=normal)
        write_texture_import(target, normal_map=normal)
        maps[name] = target
    markings = ATLAS / "T_PSG_Markings_BaseColor.png"
    if not markings.exists():
        convert(src / "T_PSG_Markings_BaseColor.png", markings, 2048)
    write_texture_import(markings)
    surface = write_material("psg_surface", "M_PSG_Surface", albedo=maps["BaseColor"], orm=maps["ORM"],
                             normal=maps["NormalGL"], emission=maps["Emissive"], emission_energy=1.5)
    marks = write_material("psg_markings", "M_PSG_Markings", albedo=markings, roughness=0.69, metallic=0.12)
    return {"M_PSG_Surface": surface, "M_PSG_Markings": marks}


def convert_asset(name, materials):
    key, folder, role, scale, pivot_rule = ASSETS[name]
    bpy.ops.wm.open_mainfile(filepath=str(KIT / "Assets" / name / f"{name}.blend"))
    game = [o for o in objects_in("01_GAME_MESHES") if o.type == "MESH"]
    lods = {o.name: o for o in objects_in("04_LOD_REFERENCE") if o.type == "MESH"}
    hulls = [o for o in objects_in("03_UCX_COLLISION") if o.type == "MESH"]
    sockets = {o.name[len("SOCKET_"):]: o for o in objects_in("06_SOCKET_REFERENCE") if o.name.startswith("SOCKET_")}
    conv = Conversion(key)
    prefix = f"SM_{name}_"
    for o in game:
        part = o.name[len(prefix):] if o.name.startswith(prefix) else "Body"
        chain = [o] + [lods[f"{o.name}_LOD{i}"] for i in (1, 2) if f"{o.name}_LOD{i}" in lods]
        own_hulls = sorted((h for h in hulls if h.name.startswith(f"UCX_{o.name}_")), key=lambda h: h.name)
        conv.add_part(part, o.matrix_world.translation, chain, own_hulls)
        log(f"{key}: {part}: {len(chain)} LODs, {len(own_hulls)} hulls")
    for sname, s in sorted(sockets.items()):
        conv.add_socket(snake(sname), s.matrix_world.copy())

    turn = TURN_HELD if role == "held" else TURN_WORLD
    if pivot_rule == "centre":
        lo, hi = world_bounds([r["lods"][0] for r in conv.parts.values()])
        pivot = (lo + hi) * 0.5
    elif pivot_rule:
        pivot = sockets[pivot_rule].matrix_world.translation.copy()
    else:
        pivot = Vector((0, 0, 0))
    matrix = turn @ Matrix.Scale(scale, 4) @ Matrix.Translation(-pivot)
    conv.finish(matrix)
    return conv.export(folder / f"{key}.glb", materials)


def main():
    names = script_args() or list(ASSETS)
    materials = atlas_materials()
    for name in names:
        convert_asset(name, materials)


main()
