"""The four door items, made from the corrected door kit (docs/ITEM_ART_DOORS.md).

wooden-door, armoured-door, double-door and garage-door are the doors the building draws (game/assets/building/doors_*.glb and
garage_door.glb, written by art/import/door_kit.py: symmetric leaves lying inside a frame centred in the 0.3 m wall): the
item's model is the same frame and leaf (or leaves, or the garage's curtain) shut, assembled the way the building assembles
them (the leaves' hinges are building.json's), so a door in the inventory is the door that is placed. Nothing is drawn here;
the kit's meshes and Godot materials are used as they are. The item is dropped at 0.3 scale and shown in the inventory, so it is
far lighter than the placed door: each of its levels is built from the kit's next lighter one (its LOD0 from the kit's LOD1,
its LOD1 from the kit's LOD2), and reduced further, by collapse decimation, only where a level is over its budget below.

World objects face Blender -Y (Godot +Z, the outside) with the ground at z = 0. The pivots are the earlier door models':
a single or a double door hangs from its lower left hinge corner at the origin and closes across +X, the garage door's
origin is the middle of its bottom edge; the wall's middle plane is the plane z = 0 (Godot) for all of them. Each leaf is
a part of its own (`Leaf`, or `Left` and `Right`; the garage's is `Curtain`), an empty at its hinge pin, as the doors that
move are; the frame is `Body`.

    blender -b --factory-startup --python art/models/doors.py -- [keys...]
"""
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from hollow import ASSETS, MATERIALS, WORLD, log, script_args  # noqa: E402
from hollow.export import collection, convex_hull, copy_object, export_glb, reset, select_only  # noqa: E402

BUILDING = ASSETS / "building"
# The kit's level each item level is made from: the item's LOD0 is the kit's LOD1 (the kit's full detail is for the placed door).
LEVELS = ("_LOD1", "_LOD2", "_LOD2")
BUDGETS = (6000, 2000, 600)

# Where each kit leaf hangs in its frame (building.json "doors": leaves[].hinge, Godot axes: x across, y up, z the outside).
# A leaf's own origin is its hinge pin on its middle plane.
SINGLE_HINGE = Vector((-0.562, 0.012, 0.0))
DOUBLE_HINGES = (Vector((-1.312, 0.012, 0.0)), Vector((1.312, 0.012, 0.0)))
# The lock's seat on the first leaf (Doors.LockOffset): 0.86 of the leaf's width along from the hinge, 1.05 m up, lockDepth out
# of the leaf's middle plane; and where someone using the door stands, in front of it.
LOCK_ALONG, LOCK_UP, LOCK_DEPTH = 0.86, 1.05, 0.12
USE_OUT = 0.8

# What each item is built from and its budgets (triangles at each level of detail). wooden-door and armoured-door are the Wood
# and Armored single doors; double-door places the *sheet metal* double door (items.json: "double-door" is "Sheet metal double
# door"), so it is the kit's Metal double door; garage-door is the garage frame with its curtain shut.
ITEMS = {
    "wooden-door": {
        "kit": "doors_wood.glb", "material": "Building_wood", "frame": "Wood_SingleDoor_Frame",
        "leaves": [("Leaf", "Wood_SingleDoor_Left", SINGLE_HINGE)], "width": 1.144, "budgets": BUDGETS,
        "pivot": SINGLE_HINGE.x, "hinge_socket": True,
    },
    "armoured-door": {
        "kit": "doors_armored.glb", "material": "Building_armoured", "frame": "Armored_SingleDoor_Frame",
        "leaves": [("Leaf", "Armored_SingleDoor_Left", SINGLE_HINGE)], "width": 1.144, "budgets": BUDGETS,
        "pivot": SINGLE_HINGE.x, "hinge_socket": True,
    },
    "double-door": {
        "kit": "doors_metal.glb", "material": "Building_metal", "frame": "Metal_DoubleDoor_Frame",
        "leaves": [("Left", "Metal_DoubleDoor_Left", DOUBLE_HINGES[0]), ("Right", "Metal_DoubleDoor_Right", DOUBLE_HINGES[1])],
        "width": 1.322, "budgets": BUDGETS, "pivot": DOUBLE_HINGES[0].x, "hinge_socket": False,
    },
    "garage-door": {
        "kit": "garage_door.glb", "material": "Door_garage", "frame": "GarageDoor_Frame", "curtain": "GarageDoor_Curtain",
        # The curtain's building.json hinge (z 0.2 is the curtain's middle plane) and lockDepth: the seat of its lock, on the
        # curtain's outer face.
        "lock_hinge": Vector((-1.35, 0.0, 0.2)), "lock_depth": 0.048, "width": 2.7, "budgets": BUDGETS,
        "pivot": 0.0, "hinge_socket": False, "use_out": 0.9,
    },
}
MATERIAL_FILES = {
    "Building_wood": MATERIALS / "building_wood.tres",
    "Building_armoured": MATERIALS / "building_armoured.tres",
    "Building_metal": MATERIALS / "building_metal.tres",
    "Door_garage": MATERIALS / "door_garage.tres",
}


def blender(v):
    """A Godot-axes point (x, y up, z the outside) in Blender's (x, -z, y)."""
    return Vector((v[0], -v[2], v[1]))


def triangles(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def placed(source, name, target, move):
    """A copy of a kit mesh, its object transform baked into the mesh and the mesh moved by `move` (Blender axes)."""
    o = copy_object(source, name, target)
    o.data.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)
    o.data.transform(Matrix.Translation(move))
    return o


def reduce(o, ratio):
    """Collapse-decimates a mesh in place to `ratio` of its triangles."""
    mod = o.modifiers.new("lod", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    select_only([o])
    bpy.ops.object.modifier_apply(modifier=mod.name)
    o.data.validate(verbose=False)


def fit(parts, budget, label):
    """Reduces every part of a level by the same ratio until the level is within its budget."""
    total = sum(triangles(o) for o in parts)
    if total <= budget:
        return
    ratio = budget / total
    for o in parts:
        reduce(o, ratio)
    log(f"{label}: {total} -> {sum(triangles(o) for o in parts)} triangles (parts at {ratio:.2f})")


def curtain_at_rest(kit, name, target):
    """The garage curtain's mesh as the rig holds it at rest, which is shut (the kit's Close clip ends there), as a plain mesh."""
    rig = kit["GarageDoor_Rig"]
    curtain = kit["GarageDoor_Curtain"]
    rig.animation_data_clear()
    rig.data.pose_position = "REST"
    depsgraph = bpy.context.evaluated_depsgraph_get()
    depsgraph.update()
    mesh = bpy.data.meshes.new_from_object(curtain.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
    mesh.transform(curtain.matrix_world)
    mesh.name = name
    o = bpy.data.objects.new(name, mesh)
    target.objects.link(o)
    return o


def part(out, name, pivot, chain):
    """A moving part: an empty at `pivot` holding its levels of detail, whose meshes are then relative to it."""
    empty = bpy.data.objects.new(name, None)
    out.objects.link(empty)
    empty.location = pivot
    for o in chain:
        o.data.transform(Matrix.Translation(-pivot))
        o.parent = empty
    return empty


def socket(out, name, godot_at):
    e = bpy.data.objects.new(f"socket_{name}", None)
    e.empty_display_size = 0.03
    out.objects.link(e)
    e.location = blender(godot_at)
    return e


def build(key):
    spec = ITEMS[key]
    reset()
    out = collection("HOLLOW_EXPORT")
    bpy.ops.import_scene.gltf(filepath=str(BUILDING / spec["kit"]))
    kit = {o.name: o for o in bpy.data.objects}
    leaves = spec.get("leaves", [])
    # The pivot: a door's lower hinge corner (the kit's frame is centred on x = 0), the garage's bottom middle.
    origin = Vector((-spec["pivot"], 0.0, 0.0))
    shift = blender(origin)
    levels = []
    for level, suffix in enumerate(LEVELS):
        frame = placed(kit[spec["frame"] + suffix], f"Body_lod{level}", out, shift)
        if "curtain" in spec:
            curtain = curtain_at_rest(kit, f"Curtain_lod{level}", out)
            curtain.data.transform(Matrix.Translation(shift))
            moving = [("Curtain", curtain, shift)]
        else:
            moving = [(name, placed(kit[mesh + suffix], f"{name}_lod{level}", out, shift + blender(hinge)), shift + blender(hinge))
                      for name, mesh, hinge in leaves]
        # (The garage curtain is skinned in the kit and has no levels of its own: it is reduced with the rest to each budget.)
        fit([frame] + [m[1] for m in moving], spec["budgets"][level], f"{key} lod{level}")
        levels.append((frame, moving))
    # One convex hull around the whole door at its closest level, in the item's frame (before the parts move to their pivots).
    hull = convex_hull([levels[0][0]] + [m[1] for m in levels[0][1]], "Body_hull0-convcolonly", out, max_faces=32)
    objects = [frame for frame, _ in levels] + [hull]
    # Each moving part (a leaf, the curtain) hangs from an empty at its pivot, as the doors that move do.
    for index, (name, _, pivot) in enumerate(levels[0][1]):
        chain = [moving[index][1] for _, moving in levels]
        objects += [part(out, name, pivot, chain)] + chain
    # Sockets: the lock on the first leaf's free edge, where someone stands to use the door, a single door's hinge pin.
    lock_hinge = spec.get("lock_hinge", leaves[0][2] if leaves else Vector((0.0, 0.0, 0.0)))
    lock = origin + lock_hinge + Vector((LOCK_ALONG * spec["width"], LOCK_UP, spec.get("lock_depth", LOCK_DEPTH)))
    use_x = lock.x if len(leaves) == 1 else origin.x + (sum(h.x for _, _, h in leaves) / 2 if leaves else 0.0)
    objects.append(socket(out, "lock", lock))
    objects.append(socket(out, "use", Vector((use_x, 1.1, spec.get("use_out", USE_OUT)))))
    if spec["hinge_socket"]:
        objects.append(socket(out, "hinge", origin + leaves[0][2]))
    path = export_glb(WORLD / f"{key}.glb", objects, {spec["material"]: MATERIAL_FILES[spec["material"]]})
    counts = [triangles(frame) + sum(triangles(m[1]) for m in moving) for frame, moving in levels]
    log(f"{key}: {path.name} triangles per level {counts} (budgets {list(spec['budgets'])}), hull {triangles(hull)}")


for key in script_args() or list(ITEMS):
    build(key)
