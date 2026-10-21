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

Doors are made symmetric (docs/DOORS.md §2): a leaf's front face (the kit's detailed one) is copied onto its back about
the leaf's mid-plane, which becomes z = 0 (the hinge pin's plane and the wall's centre); the frame insert is mirrored the
same way about its own middle and so sits centred in the 0.3 m wall; every single door also gets a right-hinged leaf, the
left one mirrored in X (the double doors have the kit's own left and right). The garage door is left as the kit has it.

Materials: the door kit's wood, metal and armoured texture sets are the building kit's own files, so doors use the
grades' materials (Building_wood, _metal, _armoured); the garage has its own (Door_garage, with the metal normal map);
the glass pane is translucent (Door_glass). Textures live in game/assets/building/textures (kit_common.texture).
"""
import pathlib
import sys

import bmesh
import bpy
import mathutils

# No __pycache__ beside the scripts in the repository.
sys.dont_write_bytecode = True
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


# The kit's own planes, in metres along the wall's normal (+Z toward the outside once in Godot's axes). The leaves' slab runs
# z -0.098 to 0.002 (planks on the front face up to 0.102, a flat back with a lever to -0.17); the frame inserts run
# -0.115 to 0.179, 3 cm off the wall's centre. The planes below become z = 0.
LEAF_MID = -0.048
FRAME_MID = 0.032
# A leaf is made 0.8 as deep as the kit's (planks, straps and handle plate keep their widths): the whole door then lies
# inside the 0.294 m frame with the handle plate 2.7 cm and the planks 6 cm back from its faces, as Rust's doors sit
# in their frames; the kit's leaf stood proud of the frame's outside face.
LEAF_DEPTH = 0.8


def symmetrise(o, mid, depth=1.0):
    """Keeps the half of a mesh on the outside of the plane z = mid, moves that plane to z = 0 and mirrors the half onto the
    other side (Blender's y is minus Godot's z): the part's two faces become the one that faced outside. The depth is
    then scaled by <depth>."""
    me = o.data
    me.transform(mathutils.Matrix.Translation((0.0, mid, 0.0)))
    bm = bmesh.new()
    bm.from_mesh(me)
    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=(0, 0, 0), plane_no=(0, 1, 0), clear_outer=True, clear_inner=False)
    bm.to_mesh(me)
    bm.free()
    modifier = o.modifiers.new("mirror", 'MIRROR')
    modifier.use_axis = (False, True, False)
    modifier.use_mirror_merge = True
    modifier.merge_threshold = 1e-5
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    o.select_set(False)
    if depth != 1.0:
        o.data.transform(mathutils.Matrix.Scale(depth, 4, (0, 1, 0)))


def centre_hull(o, mid, depth=1.0):
    """A kit hull (a box: two z levels) about the plane z = mid, as a box about z = 0 (of <depth> times the depth). Returns
    True for a hull wholly on the outside (the double doors' meeting stile), which then keeps its place off the plane."""
    zs = [-v.co.y for v in o.data.vertices]
    low, high = min(zs), max(zs)
    middle = (low + high) / 2
    if low > mid + 1e-4:
        low, high = (low - mid) * depth, (high - mid) * depth
        for v in o.data.vertices:
            v.co.y = -(high if -v.co.y > middle else low)
        return True
    half = (high - low) / 2 * depth
    for v in o.data.vertices:
        v.co.y = -(half if -v.co.y > middle else -half)
    return False


def mirrored_copy(o, name, axis):
    """A copy of a mesh object mirrored in X or in Z (Godot's axes), its faces turned the right way round."""
    copy = o.copy()
    copy.data = o.data.copy()
    copy.name = name
    copy.data.name = name
    bpy.context.scene.collection.objects.link(copy)
    flip = mathutils.Matrix.Scale(-1.0, 4, (1, 0, 0) if axis == 'x' else (0, 1, 0))
    copy.data.transform(flip)
    copy.data.flip_normals()
    return copy


def part(name, mat, levels=True, lines=None, mid=None, depth=1.0):
    """Imports a kit part with its levels of detail; its hulls become Godot collision; returns its triangle counts. With
    <mid>, the part is made symmetric about that plane and <depth> times as deep (symmetrise)."""
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
                    if mid is not None:
                        if centre_hull(o, mid, depth):
                            copy = mirrored_copy(o, f"{name}_col{hull:02d}{kc.COLLISION_SUFFIX}", 'z')
                            copy.data.name = copy.name
                            hull += 1
                else:
                    bpy.data.objects.remove(o, do_unlink=True)
                continue
            o.name = name + suffix
            o.data.name = o.name
            kc.drop_lightmap_uvs(o)
            if mid is not None:
                symmetrise(o, mid, depth)
            kc.set_material(o, mat)
            counts.append(kc.triangles(o))
    if lines is not None:
        lines.append(f"  {name:28} triangles {counts}")
    return counts


def right_hinged(kind, lines):
    """The single door's right-hinged leaf: the left one mirrored in X, every level of detail and its hull."""
    for suffix in ("", "_LOD1", "_LOD2"):
        source = bpy.data.objects[f"{kind}_SingleDoor_Left{suffix}"]
        mirrored_copy(source, f"{kind}_SingleDoor_Right{suffix}", 'x')
    hull = bpy.data.objects[f"{kind}_SingleDoor_Left_col00{kc.COLLISION_SUFFIX}"]
    mirrored_copy(hull, f"{kind}_SingleDoor_Right_col00{kc.COLLISION_SUFFIX}", 'x')
    lines.append(f"  {kind}_SingleDoor_Right        the left leaf mirrored in X")


def doors(kind, lines):
    kc.clear_scene()
    mat = material(kind)
    lines.append(f"doors {kind}:")
    for name in (f"{kind}_SingleDoor_Frame", f"{kind}_DoubleDoor_Frame"):
        part(name, mat, lines=lines, mid=FRAME_MID)
    for name in (f"{kind}_SingleDoor_Left", f"{kind}_DoubleDoor_Left", f"{kind}_DoubleDoor_Right"):
        part(name, mat, lines=lines, mid=LEAF_MID, depth=LEAF_DEPTH)
    right_hinged(kind, lines)
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
    # `-- doors` builds the three doors' files alone (the garage and the windows are untouched by the symmetry work).
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    lines = []
    for kind in ("Wood", "Metal", "Armored"):
        doors(kind, lines)
    if "doors" not in only:
        garage(lines)
        windows(lines)
    kc.report(lines, kc.SCRATCH / "door_kit_report.txt")


if __name__ == "__main__":
    main()
