"""Iron Wake Buildable Shell R01 -> game/assets/ships/iron-wake.glb, its shared materials and 2K textures, and the
ship's data file game/data/ground/ships/iron-wake.json (docs/IRON_WAKE.md §9.1-§9.3).

    blender -b --factory-startup --python art/build.py -- iron_wake
    blender -b --factory-startup --python art/import/iron_wake.py            # the same
    blender -b --factory-startup --python art/import/iron_wake.py -- data    # only the data file (15 s)

Reads the asset's .blend (D:\\GameDev\\Assets\\IronWake_Buildable_R01, never modified): the game meshes
(01_GAME_LOD0, 04_GAME_LOD1_2_3), the convex collision (03_CONVEX_COLLISION) and the attach points
(05_ATTACHMENT_POINTS). The model faces Godot +Z (the nose), +Y up, from the asset's origin on the ground plane.

The model:
- Body_lod0..4: every static group merged by material at each level (one draw call per material), LOD4 a decimated
  shell of what can be seen from outside, in one material whose vertex colour is each finish's average colour.
- Hold_lod0..3: Deck A's floor, linings, bulkheads, overhead and the stair, apart from the body so occlusion culling
  can drop them from outside; Hold_lod4 is an empty node (nothing of the hold shows beyond 1.2 km).
- The moving parts, each an empty at its pivot nested as in the asset (Ramp_Main holds Ramp_Leaf; Airlock_Door; each
  station's <Station>_Yaw holds <Station>_Pitch, which holds its <Station>_Recoil_0n), with <Part>_lod0..4.
- <Part>_hull<i>-convcolonly: the asset's 358 convex pieces, by part (Body for the static ones).
- socket_<Name>: the asset's 91 attach points with their orientation (+X the documented direction, +Y up), under
  the part that carries them.
- <Part>_occluder-occonly: occluders made of the hull's big opaque faces (no glass), for Godot's occlusion culling.
- Clips CargoRamp_Open/Close, Airlock_Open/Close and the sixteen <Station>_Fire_0n, from the asset's documented
  motion (Source/weapon_motion.py, Documentation/weapon_motion.json).
- Contact darkening: occlusion by rays at LOD0 (40 per vertex, 1.2 m, the monument kit's walk-through of buried
  vertices), carried to LOD1-3, mixed into the vertex colour as lerp(1, AO, 0.42).
- The gunners' sight displays show their battery's screen (Documentation/display_artwork.json: defense tile 5, siege
  tile 6, as the asset's Source/make_ironwake.py builds them); the .blend's stations point at the pilot's navigation
  screen (tile 3), so the import moves those faces' UVs into the right tile (sight_tiles, retile).

Materials: the sixteen finishes of Documentation/material_revision.json as ORMMaterial3D (iron_wake_<finish>.tres)
on the 4K Poly Haven derivatives filtered to 2K (game/assets/textures/iron_wake), each finish's tint as its albedo
colour and its normal strength as the normal scale; the ORM's occlusion at the asset's strength (0.65) baked into the
2K red channel. Displays, glass, lamps, the drives' glow plates and the distant LOD4 material besides. Godot 4.7.2
keeps a GLB's vertex colours linear, as the file has them, so these materials read them as linear
(vertex_color_is_srgb off; the house's older materials keep it on).

The loading mechanisms' hook (IRON_WAKE.md §9.5): right after the asset is loaded, before any conversion, the import
calls build(ctx) from art/ships/iron_wake_loaders.py when that file exists. `ctx` holds:
- ctx.objects: {group: the asset's LOD0 object}, e.g. ctx.objects["Siege_Forward_Pitch"] (SM_IW_Siege_Forward_Pitch),
  in the asset's native frame (+X forward, +Z up, metres) at the parked pose (yaw 0, pitch 0, barrels run out);
- ctx.lods: {group: [LOD0, LOD1, LOD2, LOD3 objects]};
- ctx.manifest, ctx.layout, ctx.motion, ctx.revision: the asset's asset_manifest.json, gunner_station_layout.json,
  weapon_motion.json and material_revision.json;
- ctx.stations: {station id: its manifest entry merged with its articulated turret};
- ctx.materials: {finish: bpy material}, the sixteen finishes and "Markings", "Glass" and "Lamps";
- ctx.collection: the collection new objects go in (only objects in it are picked up);
- ctx.source: the asset's folder; ctx.log(...).
What it adds:
- A moving piece is an EMPTY in ctx.collection at its pivot, its local axes the motion's, named
  <Station>_<Piece>[_nn] (e.g. Siege_Forward_LoaderArm_00), parented (keeping its transform) to the asset object it
  rides on (SM_IW_<Station>_Yaw, _Pitch or _Recoil_0n) or to another such empty. It becomes a part of that name.
- Its meshes are MESH objects parented to it, named <Empty>_LOD0 .. _LOD3 (LOD1-3 optional: missing levels are
  decimated from the one above); LOD4 is always made by the import.
- A mesh parented straight to an asset object and named <anything>_LOD<n> joins that part's LOD n.
- Faces use ctx.materials only, so every surface maps to a shared Godot material.
- Optional: UCX_<anything> meshes parented to an empty become its collision hulls; SOCKET_<Name> empties become
  socket_<Name>.
- build() may return a dict; it is written under "loaders" in the data file. Every added part is listed in "parts".
"""
import importlib.util
import json
import math
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from hollow import ASSETS, GAME, SOURCES, TEXTURES, log, script_args  # noqa: E402
from hollow.export import collection, copy_object, export_glb, keep_uv, select_only, set_export_colour  # noqa: E402
from hollow.godot import write_material  # noqa: E402
from hollow.kit import TURN_WORLD, snake  # noqa: E402
from hollow.large import (Triangles, area, batch_remove, carry_ao, contact_ao, decimate_to,  # noqa: E402
                          delete_faces, drop_unused_slots, face_colours, linear_to_srgb, outside_faces,
                          srgb_to_linear_array, surfaces, triangles, voxel_shell, write_json)
from hollow.textures import downscale, read, renormalise, write_png, write_texture_import  # noqa: E402

import iron_wake_data  # noqa: E402

SOURCE = SOURCES / "IronWake_Buildable_R01"
BLEND = SOURCE / "IronWake_Buildable_R01.blend"
DOCS = SOURCE / "Documentation"
LOADERS = HERE.parent / "ships" / "iron_wake_loaders.py"
OUT_TEX = TEXTURES / "iron_wake"
GLB = ASSETS / "ships" / "iron-wake.glb"
DATA = GAME / "data" / "ground" / "ships" / "iron-wake.json"

AO_SAMPLES = 40
AO_DISTANCE = 1.2
AO_STRENGTH = 0.42
ORM_OCCLUSION = 0.65        # the asset's portable materials read the ORM's occlusion at this strength
LOD4_BODY = 5500            # triangles of the static shell at LOD4
LOD4_MOVING = 2600          # triangles of every moving part at LOD4 together
# The distant shell lacks LOD3's fine shading (normal maps, small parts' shadows and the occlusion between them): with
# the finishes' plain average it rendered 1.38 times as bright (linear) as LOD3 at 1 km in the model viewer.
LOD4_SHADE = 0.72
OCCLUDER_AREA = 1.0         # m², the smallest face the static occluder keeps
LAMP_ENERGY = 3.0
DISPLAY_ENERGY = 1.0

# Deck A's inside: drawn as a part of its own so occlusion culling can drop it from outside.
HOLD = ("DeckA_Floor", "DeckA_PortLining", "DeckA_StarboardLining", "DeckA_Bulkheads", "DeckA_Overhead", "Stairway")
SPECIAL = {"Markings": "iron_wake_displays", "Glass": "iron_wake_glass", "Lamps": "iron_wake_lamps"}
GLASS = "M_IronWake_Glass"
DISTANT = "M_IronWake_Distant"
ENGINE_GLOW = "M_IronWake_EngineGlow"


class Asset:
    """The loaded asset and everything the loaders' hook gets (see the module's docstring)."""

    def __init__(self):
        self.source = SOURCE
        self.blend = BLEND
        self.manifest = json.loads((DOCS / "asset_manifest.json").read_text(encoding="utf-8"))
        self.layout = json.loads((DOCS / "gunner_station_layout.json").read_text(encoding="utf-8"))
        self.motion = json.loads((DOCS / "weapon_motion.json").read_text(encoding="utf-8"))
        self.revision = json.loads((DOCS / "material_revision.json").read_text(encoding="utf-8"))
        self.displays = json.loads((DOCS / "display_artwork.json").read_text(encoding="utf-8"))
        self.groups = self.manifest["groups"]
        self.objects = {g: bpy.data.objects.get(f"SM_IW_{g}") for g in self.groups}
        self.lods = {g: [self.objects[g]] + [bpy.data.objects.get(f"SM_IW_{g}_LOD{n}") for n in (1, 2, 3)]
                     for g in self.groups}
        self.materials = {m["name"]: bpy.data.materials[f"M_IronWake_{m['name']}"] for m in self.revision["materials"]}
        for name in SPECIAL:
            self.materials[name] = bpy.data.materials[f"M_IronWake_{name}"]
        turrets = {t["gunner_station"]: t for t in self.manifest["features"]["weapons"]["articulated_turrets"]}
        self.stations = {s["id"]: {**s, **turrets[s["id"]]} for s in self.manifest["features"]["gunner_stations"]}
        self.collection = collection("IRON_WAKE_LOADERS")

    @staticmethod
    def log(*parts):
        log("loaders:", *parts)


def load():
    bpy.ops.wm.open_mainfile(filepath=str(BLEND))
    drop = [o for name in ("02_EDITABLE_COMPONENTS", "90_STUDIO_NOT_EXPORTED")
            for o in bpy.data.collections[name].all_objects]
    batch_remove(drop)
    batch_remove(list(bpy.data.actions))
    batch_remove([m for m in bpy.data.meshes if m.users == 0])
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    log(f"loaded {BLEND.name}: {len(bpy.data.objects)} objects")


def moving_parts(asset):
    """{part: parent part} for every part that moves, in parent-first order."""
    moving = {"Ramp_Main": None, "Ramp_Leaf": "Ramp_Main", "Airlock_Door": None}
    for sid, s in asset.stations.items():
        moving[s["yaw"]] = None
        moving[s["pitch"]] = s["yaw"]
        for r in s["recoil_nodes"]:
            moving[r] = s["pitch"]
    return moving


def run_loaders(asset):
    if not LOADERS.exists():
        log("no art/ships/iron_wake_loaders.py: the batteries go without their loading mechanisms")
        return None
    spec = importlib.util.spec_from_file_location("iron_wake_loaders", LOADERS)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    result = module.build(asset)
    bpy.context.view_layer.update()
    log(f"loaders: {len(asset.collection.objects)} objects added")
    return result


# ------------------------------------------------------------------------------------------------ materials


def texture(name, kind):
    """A 4K derivative filtered to 2K once into game/assets/textures/iron_wake (kind: colour, orm or normal)."""
    target = OUT_TEX / name.replace("_4K.png", "_2K.png")
    if not target.exists():
        px = downscale(read(SOURCE / "Textures" / name), 2048)
        if kind == "normal":
            px = renormalise(px)
        if kind == "orm" and "_PBR_" in name:
            px[..., 0] = 1 - ORM_OCCLUSION * (1 - px[..., 0])
        write_png(target, px, 3)
    write_texture_import(target, normal_map=kind == "normal")
    return target


def means(path, srgb):
    px = read(path)[..., :3]
    if srgb:
        px = srgb_to_linear_array(px)
    return px.reshape(-1, 3).mean(axis=0)


def vram(path, normal):
    """Bytes of a 2048 px block-compressed texture with mipmaps (BC1 for colour and ORM, BC5 for normal maps)."""
    return int(2048 * 2048 * (1.0 if normal else 0.5) * 4 / 3)


def materials(asset):
    """Writes the shared materials; returns ({Blender material: .tres}, {material: (albedo linear, rough, metal)},
    {texture path: bytes in video memory})."""
    tex, info, mem = {}, {}, {}
    rev = asset.revision
    for m in rev["materials"]:
        files = m["files"]
        albedo = texture(files["base_color"], "colour")
        orm = texture(files["orm"], "orm")
        normal = texture(files["normal_gl"], "normal")
        for p, n in ((albedo, False), (orm, False), (normal, True)):
            mem[p] = vram(p, n)
        tint = m["tint"]
        name = f"M_IronWake_{m['name']}"
        tex[name] = write_material(
            f"iron_wake_{snake(m['name'])}", name, albedo=albedo, orm=orm, normal=normal,
            colour=tuple(linear_to_srgb(c) for c in tint) + (1,), vertex_colour=True, vertex_colour_srgb=False, ao=True,
            normal_scale=m["normal_strength"])
        a = means(albedo, True) * np.array(tint)
        o = means(orm, False)
        info[name] = (a, float(o[1]), float(o[2]))
    # The instrument displays (the asset's markings atlas, now only screens): lit by their emissive art, tinted as the
    # asset's emissive factor; their energy is the ship's power to drive (iron-wake.json "materials"). A map that is
    # uniform over the whole image is dropped for its value: the atlas's normal map is flat (the screens are smooth),
    # 5.6 MB less in video memory for the same look; an ORM that were uniform would become roughness and metalness.
    src_orm = read(SOURCE / "Textures" / "T_IronWake_Markings_ORM.png")[..., :3]
    src_nrm = read(SOURCE / "Textures" / "T_IronWake_Markings_NormalGL.png")[..., :3]
    uniform_orm = float(src_orm.reshape(-1, 3).std(axis=0).max()) < 0.01
    flat_normal = float(np.abs(src_nrm.reshape(-1, 3) - np.array([0.5, 0.5, 1.0])).max()) < 0.02
    kinds = [("BaseColor", "colour"), ("Emissive", "colour")]
    kinds += [] if uniform_orm else [("ORM", "orm")]
    kinds += [] if flat_normal else [("NormalGL", "normal")]
    mk = {k: texture(f"T_IronWake_Markings_{k}.png", kind) for k, kind in kinds}
    for k, p in mk.items():
        mem[p] = vram(p, k == "NormalGL")
    o = src_orm.reshape(-1, 3).mean(axis=0) if uniform_orm else means(mk["ORM"], False)
    tex["M_IronWake_Markings"] = write_material(
        SPECIAL["Markings"], "M_IronWake_Markings", albedo=mk["BaseColor"], orm=mk.get("ORM"),
        normal=mk.get("NormalGL"), emission=mk["Emissive"], emission_energy=DISPLAY_ENERGY, vertex_colour=True,
        vertex_colour_srgb=False, ao="ORM" in mk, roughness=round(float(o[1]), 3), metallic=round(float(o[2]), 3),
        emission_colour=tuple(linear_to_srgb(c) for c in (0.45, 1.0, 0.6)))
    info["M_IronWake_Markings"] = (means(mk["BaseColor"], True), float(o[1]), float(o[2]))
    glass = (0.10, 0.20, 0.22)
    tex[GLASS] = write_material(
        SPECIAL["Glass"], GLASS, colour=tuple(linear_to_srgb(c) for c in glass) + (0.22,), roughness=0.065,
        metallic=0.0, vertex_colour=False, transparent=True)
    info[GLASS] = (np.array(glass), 0.065, 0.0)
    lamp = (0.72, 0.53, 0.28)
    tex["M_IronWake_Lamps"] = write_material(
        SPECIAL["Lamps"], "M_IronWake_Lamps", colour=tuple(linear_to_srgb(c) for c in lamp) + (1,), roughness=0.25,
        metallic=0.0, vertex_colour=True, vertex_colour_srgb=False, emission_energy=LAMP_ENERGY,
        emission_colour=tuple(linear_to_srgb(c) for c in (1.0, 0.66, 0.32)))
    info["M_IronWake_Lamps"] = (np.array(lamp), 0.25, 0.0)
    # The drives' glow plates: the lamps' faces inside the nozzles, sooty steel when cold, lit with the drives' thrust
    # (iron-wake.json "materials"; off at rest).
    soot = (0.045, 0.042, 0.04)
    tex[ENGINE_GLOW] = write_material(
        "iron_wake_engine_glow", ENGINE_GLOW, colour=tuple(linear_to_srgb(c) for c in soot) + (1,), roughness=0.55,
        metallic=0.7, vertex_colour=True, vertex_colour_srgb=False, emission_energy=0.0,
        emission_colour=tuple(linear_to_srgb(c) for c in (1.0, 0.62, 0.3)))
    info[ENGINE_GLOW] = (np.array(soot), 0.55, 0.7)
    return tex, info, mem


# ------------------------------------------------------------------------------------------------ geometry


def bake_world(objects, matrix):
    """Moves objects by `matrix` and bakes every transform into their meshes (identity object transforms)."""
    for o in objects:
        o.matrix_world = matrix @ o.matrix_world
    select_only(objects)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def retile(o, tile, material="M_IronWake_Markings", grid=4):
    """Moves the UVs of `o`'s faces of `material` into atlas tile `tile` (counted from the top left, row by row), from
    whatever tile each face is in now."""
    me = o.data
    names = [m.name if m else "" for m in me.materials]
    if material not in names:
        return 0
    idx = names.index(material)
    uv = me.uv_layers[0].data
    col, row = tile % grid, tile // grid
    moved = 0
    for p in me.polygons:
        if p.material_index != idx:
            continue
        us = [uv[li].uv[0] for li in p.loop_indices]
        vs = [uv[li].uv[1] for li in p.loop_indices]
        c0 = min(grid - 1, max(0, int(sum(us) / len(us) * grid)))
        r0 = grid - 1 - min(grid - 1, max(0, int(sum(vs) / len(vs) * grid)))
        du, dv = (col - c0) / grid, -(row - r0) / grid
        for li in p.loop_indices:
            uv[li].uv = (uv[li].uv[0] + du, uv[li].uv[1] + dv)
        moved += 1
    return moved


def merged(sources, name, out, tiles=None):
    """One mesh object in the game-turned frame from copies of `sources` (each at its world transform). `tiles`:
    {source object name: the display atlas tile its screens show}."""
    copies = []
    glow = bpy.data.materials.get(ENGINE_GLOW) or bpy.data.materials.new(ENGINE_GLOW)
    for i, s in enumerate(sources):
        c = copy_object(s, f"{name}.{i:03d}", out)
        keep_uv(c, "UV0_Material")
        for a in list(c.data.color_attributes):
            c.data.color_attributes.remove(a)
        if tiles and s.name in tiles:
            retile(c, tiles[s.name])
        # The glow plates inside the drives' nozzles burn with the drives, not with the ship's lamps.
        if s.name.startswith("SM_IW_Engine_"):
            for k, m in enumerate(c.data.materials):
                if m is not None and m.name == "M_IronWake_Lamps":
                    c.data.materials[k] = glow
        copies.append(c)
    bake_world(copies, TURN_WORLD)
    if len(copies) > 1:
        select_only(copies)
        bpy.context.view_layer.objects.active = copies[0]
        bpy.ops.object.join()
    o = copies[0]
    o.name = name
    o.data.name = name
    drop_unused_slots(o)
    return o


def attach(o, empty):
    """Parents a world-space mesh object (identity transform) under `empty`, its mesh in the empty's frame."""
    o.data.transform(empty.matrix_world.inverted())
    o.parent = empty
    o.matrix_parent_inverse = Matrix.Identity(4)
    o.matrix_basis = Matrix.Identity(4)


def make_empty(name, world, out, parent=None, size=0.2):
    """An empty at `world`, under `parent` with its local transform computed here (not from a stale parent)."""
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = size
    out.objects.link(e)
    if parent is not None:
        e.parent = parent
        e.matrix_parent_inverse = Matrix.Identity(4)
        e.matrix_basis = parent.matrix_world.inverted() @ world
    else:
        e.matrix_basis = world
    bpy.context.view_layer.update()
    return e


def loader_parts(asset, moving):
    """The loaders' added pieces: [(name, parent part, empty, {level: mesh}, [hulls], [sockets])] parent-first, and
    {asset part: {level: [meshes]}} for meshes added straight to an asset part."""
    objs = list(asset.collection.all_objects)
    parts, additions = [], {}

    def part_of(obj):
        if obj is None:
            return None
        if obj.name.startswith("SM_IW_") and obj.name[6:] in asset.groups:
            return obj.name[6:]
        return obj.name if obj in objs and obj.type == "EMPTY" else None
    empties = [o for o in objs if o.type == "EMPTY" and not o.name.startswith("SOCKET_")]
    order, placed = [], set(moving)
    while len(order) < len(empties):
        progress = False
        for e in empties:
            if e.name in placed:
                continue
            parent = part_of(e.parent)
            if parent is None:
                raise RuntimeError(f"loaders: {e.name} must be parented to an asset part or another loader empty")
            if parent in placed or parent in asset.groups:
                order.append((e, parent))
                placed.add(e.name)
                progress = True
        if not progress:
            raise RuntimeError("loaders: a cycle in the loader empties' parents")
    for e, parent in order:
        lods, hulls, sockets = {}, [], []
        for c in e.children:
            if c.type == "MESH" and c.name.startswith("UCX_"):
                hulls.append(c)
            elif c.type == "MESH":
                level = int(c.name.rsplit("_LOD", 1)[1]) if "_LOD" in c.name else 0
                lods[level] = c
            elif c.type == "EMPTY" and c.name.startswith("SOCKET_"):
                sockets.append(c)
        if 0 not in lods:
            raise RuntimeError(f"loaders: {e.name} has no {e.name}_LOD0 mesh")
        parts.append((e.name, parent, e, lods, hulls, sockets))
    by_base = {}
    for o in objs:
        if o.type == "MESH" and o.parent is not None and part_of(o.parent) in asset.groups and "_LOD" in o.name:
            base, level = o.name.rsplit("_LOD", 1)
            by_base.setdefault((o.parent.name[6:], base), {})[int(level)] = o
    # A static addition with fewer levels than four gets the missing ones decimated from the level above.
    scratch = collection("IRON_WAKE_SCRATCH")
    for (part, base), levels in sorted(by_base.items()):
        if 0 not in levels:
            raise RuntimeError(f"loaders: {base} has no {base}_LOD0 mesh")
        for level in range(4):
            if level not in levels:
                levels[level] = decimated_copy(levels[level - 1], f"{base}_LOD{level}.fill", (0.5, 0.25, 0.6)[level - 1],
                                               scratch)
            additions.setdefault(part, {}).setdefault(level, []).append(levels[level])
    return parts, additions


def decimated_copy(o, name, ratio, out):
    c = copy_object(o, name, out)
    if ratio < 0.999 and len(c.data.polygons) > 24:
        mod = c.modifiers.new("lod", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        mod.use_collapse_triangulate = True
        select_only([c])
        bpy.ops.object.modifier_apply(modifier=mod.name)
        c.data.validate(verbose=False)
    return c


def sight_tiles(asset):
    """{asset object: atlas tile} for the gunners' sight displays: their battery's screen as display_artwork.json binds
    the roles (defense_gunner 5, siege_gunner 6) and the asset's Source/make_ironwake.py builds them; the .blend's
    stations point at the pilot's navigation screen (tile 3) instead."""
    by_role = {t["role"]: t["tile"] for t in asset.displays["tiles"]}
    out = {}
    for s in asset.stations.values():
        tile = by_role.get(f"{s['weapon_class']}_gunner")
        for o in asset.lods[s["yaw"]] if tile is not None else []:
            if o is not None:
                out[o.name] = tile
    return out


def build(asset, moving):
    """The export objects: {part: {"empty", "lods": [objects], "hulls": []}}, plus root objects."""
    out = collection("HOLLOW_EXPORT")
    parts = {}
    tiles = sight_tiles(asset)
    statics = [g for g in asset.groups if g not in moving and asset.objects[g] is not None]
    body = [g for g in statics if g not in HOLD]
    hold = [g for g in statics if g in HOLD]
    loaders, additions = loader_parts(asset, moving)
    for level in range(4):
        parts.setdefault("Body", {"empty": None, "lods": [], "hulls": []})["lods"].append(
            merged([asset.lods[g][level] for g in body] + additions.get("Body", {}).get(level, []),
                   f"Body_lod{level}", out))
        parts.setdefault("Hold", {"empty": None, "lods": [], "hulls": []})["lods"].append(
            merged([asset.lods[g][level] for g in hold], f"Hold_lod{level}", out))
    for name, parent in moving.items():
        # The pivot only: the part's axes are the model's (its motion is about or along them, IRON_WAKE.md §9.1).
        pivot = Matrix.Translation(TURN_WORLD @ Vector(asset.groups[name]["origin"]))
        empty = make_empty(name, pivot, out, parts[parent]["empty"] if parent else None)
        rec = parts[name] = {"empty": empty, "lods": [], "hulls": [], "parent": parent}
        for level in range(4):
            o = merged([asset.lods[name][level]] + additions.get(name, {}).get(level, []), f"{name}_lod{level}", out,
                       tiles)
            attach(o, empty)
            rec["lods"].append(o)
    loader_records = []
    for name, parent, e, lods, hulls, sockets in loaders:
        loader_records.append(loader_record(name, parent, e))
        # The export's empty takes the piece's name; the hook's own object steps aside.
        e.name = f"{name}.hook"
        world = TURN_WORLD @ e.matrix_world
        loc, rot, _ = world.decompose()
        empty = make_empty(name, Matrix.LocRotScale(loc, rot, Vector((1, 1, 1))), out, parts[parent]["empty"])
        rec = parts[name] = {"empty": empty, "lods": [], "hulls": hulls, "parent": parent, "sockets": sockets}
        last = None
        for level in range(4):
            src = lods.get(level)
            if src is not None:
                o = merged([src], f"{name}_lod{level}", out)
            else:
                o = decimated_copy(last, f"{name}_lod{level}", (0.5, 0.25, 0.6)[level - 1], out)
            last = o
            rec["lods"].append(o)
        for o in rec["lods"]:
            if o.parent is None:
                attach(o, empty)
    return out, parts, loader_records


def loader_record(name, parent, e):
    """A loader part for the data file: its pivot and local axes at rest, in game axes."""
    native = e.matrix_world
    return {"name": name, "parent": parent, "kind": "loader", "pivot": iron_wake_data.gp(native.translation, 4),
            "axes": {"x": iron_wake_data.gp(native.col[0].xyz.normalized(), 4),
                     "y": iron_wake_data.gp(native.col[1].xyz.normalized(), 4),
                     "z": iron_wake_data.gp(native.col[2].xyz.normalized(), 4)}}


# ------------------------------------------------------------------------------------------------ occlusion and LOD4


def contact_darkening(parts):
    levels = [[rec["lods"][lv] for rec in parts.values()] for lv in range(4)]
    t0 = time.time()
    occ0 = Triangles(levels[0], skip_materials={GLASS})
    ao = contact_ao(levels[0], occ0, AO_SAMPLES, AO_DISTANCE)
    log(f"occlusion LOD0: {sum(len(v) for v in ao.values())} vertices in {time.time() - t0:.0f} s")
    for lv in (1, 2, 3):
        ao.update(carry_ao(occ0, ao, levels[lv]))
    for o, values in ao.items():
        shade = 1 - AO_STRENGTH + AO_STRENGTH * values
        rgba = np.ones((len(values), 4), np.float32)
        rgba[:, 0] = rgba[:, 1] = rgba[:, 2] = shade
        set_export_colour(o, rgba, "POINT")
    return ao


def distant_material(info, weights):
    """LOD4's one material. Metals are folded into the colour (a rough metal under the sky reads near its own albedo
    from a kilometre), so it is a dielectric with the finishes' roughness averaged by the area they cover."""
    total = max(sum(weights.values()), 1e-9)
    rough = sum(info.get(m, (None, 0.6, 0.0))[1] * w for m, w in weights.items()) / total
    return write_material("iron_wake_distant", DISTANT, roughness=round(rough, 3), metallic=0.0,
                          vertex_colour=True, vertex_colour_srgb=False), rough, 0.0


def split_faces(o, name, out, material):
    """Moves the faces of `material` from `o` into a new object `name` (same transform); None when there are none."""
    names = [m.name if m else "" for m in o.data.materials]
    if material not in names:
        return None
    idx = names.index(material)
    part = copy_object(o, name, out)
    for target, keep_it in ((o, lambda f: f.material_index != idx), (part, lambda f: f.material_index == idx)):
        bm = bmesh.new()
        bm.from_mesh(target.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if not keep_it(f)], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(target.data)
        bm.free()
    return part


def lod4(parts, info, out, ao):
    """LOD4 of every part but the hold, from its LOD3: the closed surface round the union of its pieces (a voxel
    remesh, so overlapping pieces and thin shells such as the barrels' heat shields fuse instead of folding into
    slivers), only what can be seen from outside (the glass blocking the view in), decimated; each face coloured by the
    finish of the nearest LOD3 face (its average colour) times the contact darkening there; one material."""
    names = [n for n in parts if n != "Hold"]
    src3 = {n: parts[n]["lods"][3] for n in names}
    source = Triangles([src3[n] for n in names])
    shade = np.ones(len(source.verts), np.float32)
    for o, (base, n) in source.ranges.items():
        shade[base:base + n] = 1 - AO_STRENGTH + AO_STRENGTH * ao[o]
    grey = (np.array((0.2, 0.2, 0.2)), 0.6, 0.0)
    rgb = np.array([info.get(m, grey)[0] for m in source.materials]) * shade[source.tris].mean(axis=1)[:, None]
    rgb *= LOD4_SHADE
    tv = source.verts[source.tris]
    areas = 0.5 * np.linalg.norm(np.cross(tv[:, 1] - tv[:, 0], tv[:, 2] - tv[:, 0]), axis=1)
    weights = {}
    for m, a in zip(source.materials, areas.tolist()):
        weights[m] = weights.get(m, 0.0) + a
    shells, panes = {}, []
    for n in names:
        o = copy_object(src3[n], f"{n}_lod4", out)
        select_only([o])
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        # Thin panes would vanish in the shell: the glass stays as it is, beside it.
        glass = split_faces(o, f"{n}_lod4_glass", out, GLASS)
        if glass is not None:
            panes.append(glass)
        lo, hi = Vector(o.bound_box[0]), Vector(o.bound_box[6])
        voxel = min(0.15, max(0.025, (hi - lo).length / 400))
        voxel_shell(o, voxel, 60000 if n == "Body" else 2000)
        shells[n] = (o, glass)
    blockers = Triangles([o for o, _ in shells.values()] + panes)
    for o, _ in shells.values():
        delete_faces(o, outside_faces([o], blockers, directions=24)[o])
    # The moving parts share their budget by the area they show, so a long barrel keeps its tube.
    moving_area = sum(area(o) for n, (o, _) in shells.items() if n != "Body")
    for n, (o, glass) in shells.items():
        budget = LOD4_BODY if n == "Body" else max(48, int(LOD4_MOVING * area(o) / max(moving_area, 1e-6)))
        decimate_to(o, budget)
        if glass is not None:
            delete_faces(glass, outside_faces([glass], blockers, directions=24)[glass])
            select_only([o, glass])
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.join()
        me = o.data
        for uv in list(me.uv_layers):
            me.uv_layers.remove(uv)
        if me.has_custom_normals:
            select_only([o])
            bpy.ops.mesh.customdata_custom_splitnormals_clear()
        set_export_colour(o, face_colours(o, source, lambda i: rgb[i]), "CORNER")
        me.materials.clear()
        me.materials.append(bpy.data.materials.get(DISTANT) or bpy.data.materials.new(DISTANT))
        me.polygons.foreach_set("material_index", np.zeros(len(me.polygons), np.int32))
        select_only([o])
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
        o.name = f"{n}_lod4"
        o.data.name = o.name
        if n != "Body":
            attach_world(o, parts[n]["empty"])
        parts[n]["lods"].append(o)
    parts["Hold"]["lods"].append(make_empty("Hold_lod4", Matrix.Identity(4), out, size=0.05))
    return weights


def attach_world(o, empty):
    """Parents an object at its world transform under `empty`, its mesh in the empty's frame."""
    world = o.matrix_world.copy()
    o.data.transform(empty.matrix_world.inverted() @ world)
    o.parent = empty
    o.matrix_parent_inverse = Matrix.Identity(4)
    o.matrix_basis = Matrix.Identity(4)


# ------------------------------------------------------------------------------------------------ hulls, sockets, occluders


def hulls(asset, moving, parts, out):
    count = {}
    made = []
    for u in sorted(bpy.data.collections["03_CONVEX_COLLISION"].objects, key=lambda o: o.name):
        group = u.parent.name[len("SM_IW_"):]
        part = group if group in moving else "Body"
        i = count[part] = count.get(part, -1) + 1
        h = copy_object(u, f"{part}_hull{i}-convcolonly", out)
        bake_world([h], TURN_WORLD)
        h.data.materials.clear()
        for uv in list(h.data.uv_layers):
            h.data.uv_layers.remove(uv)
        for a in list(h.data.color_attributes):
            h.data.color_attributes.remove(a)
        if part != "Body":
            attach(h, parts[part]["empty"])
        made.append(h)
    for name, rec in parts.items():
        for j, u in enumerate(rec.get("hulls", [])):
            h = copy_object(u, f"{name}_hull{j}-convcolonly", out)
            bake_world([h], TURN_WORLD)
            h.data.materials.clear()
            attach(h, rec["empty"])
            made.append(h)
    log(f"hulls: {len(made)}")
    return made


def sockets(asset, moving, parts, out):
    made = []
    for s in asset.manifest["sockets"]:
        parent = s["parent"] if s["parent"] in asset.groups else "Hull_Underbody"
        src = bpy.data.objects[f"SOCKET_SM_IW_{parent}_{s['name']}"]
        loc, rot, _ = (TURN_WORLD @ src.matrix_world).decompose()
        holder = parts[s["parent"]]["empty"] if s["parent"] in moving else None
        made.append(make_empty(f"socket_{s['name']}", Matrix.LocRotScale(loc, rot, Vector((1, 1, 1))), out, holder,
                               0.1))
    for name, rec in parts.items():
        for src in rec.get("sockets", []):
            loc, rot, _ = (TURN_WORLD @ src.matrix_world).decompose()
            made.append(make_empty("socket_" + src.name[len("SOCKET_"):], Matrix.LocRotScale(loc, rot, Vector((1, 1, 1))),
                                   out, rec["empty"], 0.1))
    log(f"sockets: {len(made)}")
    return made


def glass_boxes(o, margin=0.05):
    """World-space boxes round each connected pane of glass."""
    me = o.data
    names = [m.name if m else "" for m in me.materials]
    if GLASS not in names:
        return []
    idx = names.index(GLASS)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    mw = o.matrix_world
    seen, boxes = set(), []
    for f in bm.faces:
        if f.material_index != idx or f.index in seen:
            continue
        stack, pts = [f], []
        seen.add(f.index)
        while stack:
            x = stack.pop()
            pts += [mw @ v.co for v in x.verts]
            for e in x.edges:
                for y in e.link_faces:
                    if y.material_index == idx and y.index not in seen:
                        seen.add(y.index)
                        stack.append(y)
        lo = Vector([min(p[i] for p in pts) - margin for i in range(3)])
        hi = Vector([max(p[i] for p in pts) + margin for i in range(3)])
        boxes.append((lo, hi))
    bm.free()
    return boxes


def occluder(sources, name, min_area, panes, out, parent=None):
    """An occluder mesh (Godot's -occonly) of the sources' big opaque faces that touch no pane of glass."""
    skip = {GLASS, "M_IronWake_Lamps", "M_IronWake_Markings"}
    verts, faces = [], []
    for o in sources:
        me = o.data
        mw = o.matrix_world
        names = [m.name if m else "" for m in me.materials]
        for p in me.polygons:
            if p.area < min_area or (names[p.material_index] if p.material_index < len(names) else "") in skip:
                continue
            pts = [mw @ me.vertices[i].co for i in p.vertices]
            lo = Vector([min(q[i] for q in pts) for i in range(3)])
            hi = Vector([max(q[i] for q in pts) for i in range(3)])
            if any(all(lo[i] <= b[1][i] and hi[i] >= b[0][i] for i in range(3)) for b in panes):
                continue
            base = len(verts)
            verts += pts
            faces.append(list(range(base, base + len(pts))))
    if not faces:
        return None
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    out.objects.link(o)
    if parent is not None:
        attach(o, parent)
    log(f"occluder {name}: {len(me.polygons)} triangles")
    return o


def occluders(parts, out):
    panes = glass_boxes(parts["Body"]["lods"][0])
    made = [occluder([parts["Body"]["lods"][3], parts["Hold"]["lods"][3]], "Body_occluder-occonly", OCCLUDER_AREA,
                     panes, out)]
    for name in ("Ramp_Main", "Ramp_Leaf", "Airlock_Door"):
        made.append(occluder([parts[name]["lods"][3]], f"{name}_occluder-occonly", 0.25, panes, out,
                             parts[name]["empty"]))
    return [o for o in made if o is not None]


# ------------------------------------------------------------------------------------------------ clips


def clip(name, tracks, fps):
    """One action named `name` with a slot per object; tracks: [(object, data path, index, times, values)], the
    values relative to the rest pose. A moved part keeps its rest offset in delta_location and its location clip
    holds the offsets alone: outside its strips Blender's NLA puts an animated channel at its default, 0, and the
    exporter would write that as the node's rest."""
    action = bpy.data.actions.new(name)
    for obj, path, index, times, values in tracks:
        if path == "location" and obj.delta_location.length == 0:
            obj.delta_location = obj.location.copy()
            obj.location = (0.0, 0.0, 0.0)
        obj.animation_data_create()
        obj.animation_data.action = action
        slot = obj.animation_data.action_slot
        if slot is None:
            slot = action.slots.new(id_type="OBJECT", name=obj.name)
            obj.animation_data.action_slot = slot
        rest = getattr(obj, path).copy()
        for t, v in zip(times, values):
            value = rest.copy()
            if isinstance(index, int):
                value[index] = v
            else:
                value = v
            setattr(obj, path, value)
            for i in ([index] if isinstance(index, int) else range(3)):
                obj.keyframe_insert(path, index=i, frame=1 + t * fps)
        setattr(obj, path, rest)
        track = obj.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, action)
        strip.action_slot = slot
        strip.extrapolation = "NOTHING"
        obj.animation_data.action = None
    for fc in _fcurves(action):
        for k in fc.keyframe_points:
            k.interpolation = "LINEAR"


def _fcurves(action):
    try:
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    yield from bag.fcurves
    except AttributeError:
        yield from action.fcurves


def clips(asset, parts):
    """The asset's motion as clips (its Source/weapon_motion.py and Documentation/weapon_motion.json): the ramp's two
    leaves about their hinges, the airlock door sliding 2.5 m forward, each barrel's recoil along the barrel."""
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    main, leaf = parts["Ramp_Main"]["empty"], parts["Ramp_Leaf"]["empty"]
    door = parts["Airlock_Door"]["empty"]
    ramp_t = [0, .36, 2.64, 3.0]
    main_a = [0, -3, -113, -114]
    leaf_a = [0, 4, 176, 180]
    for name, rev in (("CargoRamp_Open", False), ("CargoRamp_Close", True)):
        m = main_a[::-1] if rev else main_a
        lf = leaf_a[::-1] if rev else leaf_a
        clip(name, [(main, "rotation_euler", 0, ramp_t, [math.radians(a) for a in m]),
                    (leaf, "rotation_euler", 0, ramp_t, [math.radians(a) for a in lf])], fps)
    door_t = [0, .168, 1.232, 1.4]
    slide = [0, .05, 2.45, 2.5]
    for name, rev in (("Airlock_Open", False), ("Airlock_Close", True)):
        s = slide[::-1] if rev else slide
        # Native +X (forward) is Blender -Y once turned.
        clip(name, [(door, "location", 1, door_t, [-v for v in s])], fps)
    for a in asset.motion["actions"]:
        node = parts[a["recoil_node"]]["empty"]
        # Native local -X (back along the barrel) is Blender +Y once turned.
        clip(a["name"], [(node, "location", 1, a["times_seconds"], [-x for x in a["local_x_offsets_m"]])], fps)
    bpy.context.scene.frame_set(0)
    log(f"clips: {len(bpy.data.actions)}")


# ------------------------------------------------------------------------------------------------ the run


def report(parts):
    rows = []
    for lv in range(5):
        tris = moving_tris = calls = moving_calls = 0
        for name, rec in parts.items():
            if lv >= len(rec["lods"]) or rec["lods"][lv].type != "MESH":
                continue
            o = rec["lods"][lv]
            n, c = triangles(o), len(surfaces(o))
            if name in ("Body", "Hold"):
                tris += n
                calls += c
            else:
                moving_tris += n
                moving_calls += c
        rows.append((lv, tris, moving_tris, calls, moving_calls))
        log(f"LOD{lv}: {tris + moving_tris} triangles ({tris} static, {moving_tris} moving), "
            f"{calls + moving_calls} draw calls ({calls} static + {moving_calls} moving)")
    return rows


MATERIALS_INFO = {
    "lamps": {"material": "M_IronWake_Lamps", "path": "res://assets/materials/iron_wake_lamps.tres",
              "energy": LAMP_ENERGY},
    "displays": {"material": "M_IronWake_Markings", "path": "res://assets/materials/iron_wake_displays.tres",
                 "energy": DISPLAY_ENERGY},
    "glass": {"material": GLASS, "path": "res://assets/materials/iron_wake_glass.tres"},
    "exhaust": {"material": ENGINE_GLOW, "path": "res://assets/materials/iron_wake_engine_glow.tres", "energy": 0.0},
    "distant": {"material": DISTANT, "path": "res://assets/materials/iron_wake_distant.tres"},
}


def write_data(asset, moving, records, loader_result):
    data = iron_wake_data.build(asset, moving, records, MATERIALS_INFO)
    if loader_result:
        data["loaders"] = loader_result
    write_json(DATA, data)


def main():
    t0 = time.time()
    load()
    asset = Asset()
    moving = moving_parts(asset)
    loader_result = run_loaders(asset)
    if "data" in script_args():
        # Only the data file (iterating on it): blender -b --factory-startup --python art/import/iron_wake.py -- data
        loaders, _ = loader_parts(asset, moving)
        write_data(asset, moving, [loader_record(n, p, e) for n, p, e, *_ in loaders], loader_result)
        log(f"done in {time.time() - t0:.0f} s")
        return
    tex, info, mem = materials(asset)
    out, parts, loader_records = build(asset, moving)
    ao = contact_darkening(parts)
    weights = lod4(parts, info, out, ao)
    tex[DISTANT], rough, metal = distant_material(info, weights)
    hull_objs = hulls(asset, moving, parts, out)
    socket_objs = sockets(asset, moving, parts, out)
    occ = occluders(parts, out)
    clips(asset, parts)
    objects = []
    for rec in parts.values():
        if rec["empty"] is not None:
            objects.append(rec["empty"])
        objects += rec["lods"]
    objects += hull_objs + socket_objs + occ
    report(parts)
    export_glb(GLB, objects, tex, animations=True, import_extra={"meshes/force_disable_compression": "true"})
    used = {m.name for o in objects if o.type == "MESH" for m in o.data.materials if m}
    total = sum(mem.values())
    log(f"textures: {len(mem)} at 2K, {total / 2**20:.1f} MiB in video memory with mipmaps; distant roughness "
        f"{rough:.2f}, metallic {metal:.2f}")
    write_data(asset, moving, loader_records, loader_result)
    log(f"materials used: {sorted(used)}")
    log(f"done in {time.time() - t0:.0f} s")


main()
