"""PSG_DoorWindowKit_R01 -> game/assets/building/ (GROUND.md §6.3, §9).

    blender --background --factory-startup --python art/import/door_kit.py

  doors_wood.glb, doors_metal.glb, doors_armored.glb
      <Material>_SingleDoor_Frame / _Left, <Material>_DoubleDoor_Frame / _Left / _Right, each with _LOD1 and _LOD2 and its
      convex hulls (<name>_col00-convcolonly ...). A leaf's origin is its hinge; the assembly manifest's offsets and open
      yaws are in game/data/ground/building.json.
  garage_door.glb
      GarageDoor_Frame with its levels and hulls; the skinned curtain (GarageDoor_Curtain on GarageDoor_Rig) with the
      kit's Open and Close clips.
  window_inserts.glb
      Window_MetalBars, Window_ArmoredBars, Window_GlassFrame, Window_GlassPane, Window_GlassWire,
      Embrasure_Horizontal, Embrasure_Vertical, with their levels and hulls.

Materials: the door kit's wood, metal and armoured texture sets are the building kit's own files, so doors use the
grades' materials (Building_wood, _metal, _armoured); the garage has its own (Door_garage, with the metal normal map);
the glass pane is translucent (Door_glass). Textures live in game/assets/building/textures (kit_common.texture).
"""
import pathlib
import sys

import bpy

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import kit_common as kc  # noqa: E402

KIT = pathlib.Path(r"D:\GameDev\Assets\PSG_DoorWindowKit_R01")
EXPORTS = KIT / "Exports"
OUT = kc.REPO / "game" / "assets" / "building"


# The door kit's wood, metal and armoured sets are the building kit's own files: doors share the grades' materials.
GRADE = {"Wood": "wood", "Metal": "metal", "Armored": "armoured"}


def material(kind):
    t = KIT / "Textures"
    if kind in GRADE:
        return kc.pbr_material(f"Building_{GRADE[kind]}", t / f"T_PSG_{kind}_BaseColor.png", t / f"T_PSG_{kind}_NormalGL.png",
                               t / f"T_PSG_{kind}_ORM.png", prefix=f"building_{GRADE[kind]}")
    # The garage's normal map is the metal one.
    return kc.pbr_material("Door_garage", t / "T_PSG_Garage_BaseColor.png", t / "T_PSG_Garage_NormalGL.png", t / "T_PSG_Garage_ORM.png",
                           prefix="door_garage", normal_name="building_metal_NormalGL")


def glass_material():
    m = bpy.data.materials.new("Door_glass")
    m.use_nodes = True
    shader = m.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (.62, .70, .72, 1)
    shader.inputs["Roughness"].default_value = .12
    shader.inputs["Alpha"].default_value = .28
    m.blend_method = 'BLEND' if hasattr(m, "blend_method") else m.blend_method
    if hasattr(m, "surface_render_method"):
        m.surface_render_method = 'BLENDED'
    return m


def part(name, mat, levels=True, lines=None):
    """Imports a kit part with its levels of detail; its hulls become Godot collision; returns its triangle counts."""
    counts = []
    for level, suffix in ((0, ""), (1, "_LOD1"), (2, "_LOD2")) if levels else ((0, ""),):
        path = EXPORTS / f"SM_PSG_{name}{suffix}.fbx"
        if not path.exists():
            continue
        hull = 0
        for o in kc.import_fbx(path):
            if o.type != 'MESH':
                continue
            if o.name.startswith("UCX_"):
                if level == 0:
                    kc.collision(o, f"{name}_col{hull:02d}")
                    hull += 1
                else:
                    bpy.data.objects.remove(o, do_unlink=True)
                continue
            o.name = name + suffix
            o.data.name = o.name
            kc.drop_lightmap_uvs(o)
            kc.set_material(o, mat)
            counts.append(kc.triangles(o))
    if lines is not None:
        lines.append(f"  {name:28} triangles {counts}")
    return counts


def doors(kind, lines):
    kc.clear_scene()
    mat = material(kind)
    lines.append(f"doors {kind}:")
    for name in (f"{kind}_SingleDoor_Frame", f"{kind}_SingleDoor_Left", f"{kind}_DoubleDoor_Frame", f"{kind}_DoubleDoor_Left",
                 f"{kind}_DoubleDoor_Right"):
        part(name, mat, lines=lines)
    path = kc.export_glb(OUT / f"doors_{kind.lower()}.glb")
    lines.append(f"  -> {path.relative_to(kc.REPO)} ({path.stat().st_size // 1024} KB)")


def garage(lines):
    kc.clear_scene()
    mat = material("Garage")
    lines.append("garage door:")
    part("GarageDoor_Frame", mat, lines=lines)
    # The curtain, its rig and the two clips: one rig keeps the mesh, the clips' copies give their actions.
    before = {o.name for o in bpy.data.objects}
    bpy.ops.import_scene.fbx(filepath=str(EXPORTS / "SK_PSG_GarageDoor_Curtain.fbx"), axis_forward='-Y', axis_up='Z')
    imported = [bpy.data.objects[n] for n in [o.name for o in bpy.data.objects] if n not in before]
    rig = next(o for o in imported if o.type == 'ARMATURE')
    curtain = next(o for o in imported if o.type == 'MESH')
    for o in imported:
        if o.type == 'EMPTY':
            bpy.data.objects.remove(o, do_unlink=True)
    rig.name = "GarageDoor_Rig"
    curtain.name = "GarageDoor_Curtain"
    curtain.data.name = curtain.name
    kc.drop_lightmap_uvs(curtain)
    kc.set_material(curtain, mat)
    rig.animation_data_create()
    clips = []
    for clip in ("Open", "Close"):
        before = {o.name for o in bpy.data.objects}
        actions_before = set(bpy.data.actions)
        bpy.ops.import_scene.fbx(filepath=str(EXPORTS / f"A_PSG_GarageDoor_{clip}.fbx"), axis_forward='-Y', axis_up='Z')
        for name in [o.name for o in bpy.data.objects if o.name not in before]:
            bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
        action = next(a for a in bpy.data.actions if a not in actions_before)
        action.name = clip
        action.use_fake_user = True
        track = rig.animation_data.nla_tracks.new()
        track.name = clip
        strip = track.strips.new(clip, int(action.frame_range[0]), action)
        if hasattr(strip, "action_slot") and len(getattr(action, "slots", [])) > 0:
            strip.action_slot = action.slots[0]
        track.mute = True
        clips.append(f"{clip} {action.frame_range[0]:.0f}-{action.frame_range[1]:.0f}")
    lines.append(f"  GarageDoor_Curtain           triangles [{kc.triangles(curtain)}], {len(rig.data.bones)} bones, clips {', '.join(clips)}")
    path = kc.export_glb(OUT / "garage_door.glb", animations=True)
    lines.append(f"  -> {path.relative_to(kc.REPO)} ({path.stat().st_size // 1024} KB)")


def windows(lines):
    kc.clear_scene()
    metal, armoured, glass = material("Metal"), material("Armored"), glass_material()
    lines.append("window inserts:")
    for name, mat in (("Window_MetalBars", metal), ("Window_ArmoredBars", armoured), ("Window_GlassFrame", metal),
                      ("Window_GlassPane", glass), ("Window_GlassWire", metal), ("Embrasure_Horizontal", metal),
                      ("Embrasure_Vertical", armoured)):
        part(name, mat, lines=lines)
    path = kc.export_glb(OUT / "window_inserts.glb")
    lines.append(f"  -> {path.relative_to(kc.REPO)} ({path.stat().st_size // 1024} KB)")


def main():
    lines = []
    for kind in ("Wood", "Metal", "Armored"):
        doors(kind, lines)
    garage(lines)
    windows(lines)
    kc.report(lines, kc.SCRATCH / "door_kit_report.txt")


if __name__ == "__main__":
    main()
