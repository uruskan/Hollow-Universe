"""PSG Frontier AK R05 -> game/assets/items/rifle.glb, the rifle (GROUND.md §9).

The static pickup meshes LOD1-LOD3 as lod0-lod2 (the 66,652-triangle LOD0 adds nothing seen in a hand; the character
package made the same choice), the six UCX hulls and the socket contract (muzzle, eject, grips, sight) with the eye at
the kit's aiming camera. Its two texture sets go to 2K. The rifle keeps the kit's frame (+X along the barrel, Godot
+Y up) with its pivot at the right hand's grip. The weapon-only animations and the rig stay in the source for G4.

    blender -b --factory-startup --python art/import/frontier_rifle.py
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from hollow import ITEMS, RUSTMVP_ASSETS, TEXTURES  # noqa: E402
from hollow.godot import write_material  # noqa: E402
from hollow.kit import TURN_HELD, Conversion, mount_sockets, objects_in  # noqa: E402
from hollow.textures import convert, write_texture_import  # noqa: E402

SOURCE = RUSTMVP_ASSETS / "PSG_FrontierAK_R05"
OUT = TEXTURES / "frontier"


def materials():
    result = {}
    for part in ("Metal", "Furniture"):
        maps = {}
        for name, normal in (("BaseColor", False), ("ORM", False), ("NormalGL", True)):
            target = OUT / f"T_FrontierAK_{part}_{name}.png"
            if not target.exists():
                convert(SOURCE / "Textures" / f"T_FrontierAK_{part}_{name}.png", target, 2048, normal=normal)
            write_texture_import(target, normal_map=normal)
            maps[name] = target
        result[f"M_FrontierAK_{part}"] = write_material(
            f"frontier_{part.lower()}", f"M_FrontierAK_{part}", albedo=maps["BaseColor"], orm=maps["ORM"],
            normal=maps["NormalGL"], vertex_colour=False)
    return result


def main():
    mats = materials()
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / "Source" / "FrontierAK_R05_Game.blend"))
    static = {o.name: o for o in objects_in("05_STATIC_PICKUP")}
    chain = [static["SM_FrontierAK_R05_LOD1"], static["SM_FrontierAK_R05_LOD2"], static["SM_FrontierAK_R05_LOD3"]]
    hulls = sorted((o for o in objects_in("06_PICKUP_COLLISION") if o.type == "MESH"), key=lambda o: o.name)
    conv = Conversion("rifle")
    conv.add_part("Body", Vector((0, 0, 0)), chain, hulls)
    contract = json.loads((SOURCE / "Exports" / "socket_contract.json").read_text(encoding="utf-8"))
    names = {"socket_muzzle": "muzzle", "socket_eject": "eject", "socket_grip_r": "grip_r", "socket_grip_l": "grip_l",
             "socket_ads": "sight"}
    grip = None
    for s in contract["sockets"]:
        forward = Vector(s["forward_blender"]).normalized()
        up = Vector(s["up_blender"]).normalized()
        side = up.cross(forward).normalized()
        # Socket frames: +X forward, +Z up (Godot +Y).
        frame = Matrix((forward, side, forward.cross(side))).transposed().to_4x4()
        frame.translation = Vector(s["position_blender_m"])
        conv.add_socket(names.get(s["name"], s["name"]), frame)
        if s["name"] == "socket_grip_r":
            grip = Vector(s["position_blender_m"])
    cameras = {o.name: o for o in bpy.data.objects if o.type == "CAMERA"}
    conv.add_socket("eye", Matrix.Translation(cameras["CAM_ADS"].matrix_world.translation))
    conv.finish(TURN_HELD @ Matrix.Translation(-grip))
    # Attachment mounts (models.json sockets): optics on the receiver's top cover, a light under the handguard.
    mount_sockets(conv, rail_x=0.07, under_x=0.33)
    for o in conv.parts["Body"]["lods"]:
        for attr in list(o.data.color_attributes):
            o.data.color_attributes.remove(attr)
    conv.export(ITEMS / "rifle.glb", mats)


main()
