"""Gibs: the pieces things break into (GROUND.md §9.4), fractured from our own models by Blender 5.2 in background mode.

    blender -b --factory-startup --python art/gibs/build.py                         # every model with "gibs"
    blender -b --factory-startup --python art/gibs/build.py -- loot-barrel-1 building-stone   # some (catalogue keys)
    blender -b --factory-startup --python art/gibs/build.py -- --list               # what would be built, and from what

What it reads: game/data/ground/models.json, every entry with a "gibs" object (its GLB, and options: "pieces",
"hollow", "budget"); for a kit entry ("kind": "kit"), building.json names its parts: a grade's kit holds every piece
(the grade whose "model" is the kit's key), a door kit every door whose "model" is the kit's key.
What it writes: the gib file named by the entry (game/assets/gibs/<key>.glb) and its .import, which maps every material
to the shared Godot material the model itself uses; and once, the building grades' and the garage door's materials
(game/assets/materials/building_<grade>.tres, door_garage.tres), the same the building's art is drawn with.

How a model breaks (every step deterministic: a build writes the same files again):
1. The source: every part of the model in place (lids shut, door leaves on their hinges), at the finest level of detail
   within the triangle budget ("budget", 1,200 by default), decimated toward it (to half at most) if even the coarsest
   is above, in model space; coincident vertices welded so each primitive is one closed island.
2. Islands that are large enough get an inner wall when the model is hollow (a drum's sheet, "hollow": metres).
3. Seeds: points spread through the solid (halfway through it under random surface points), chosen farthest-first with
   a little chance; their number from the model's size, 1.5 + 1.6 x (its two longest sides in metres), 6 to 12, or
   "pieces".
4. Each island is cut by the Voronoi cells of the seeds (bisected by the planes halfway between two seeds) and every cut
   capped with faces of the material's inside: the island's own atlas tile, planar mapped at the material's texel
   density, its colour darkened. An island smaller than half a cell goes whole to the cell its middle is in, so bolts,
   bricks, boards and stones stay whole; with "cut": false none is cut (twig breaks into its sticks). Slivers are merged
   into their nearest piece.
5. Each piece is moved into its principal axes, so the game's contact box fits it; its node keeps where it sat, and the
   pieces put together are the model again.
"""
import json
import math
import sys
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

from hollow import ASSETS, GAME, MATERIALS, log, script_args  # noqa: E402
from hollow.export import collection, reset, select_only  # noqa: E402
from hollow.godot import write_material, write_scene_import  # noqa: E402

DATA = GAME / "data" / "ground"
BUILDING_TEXTURES = ASSETS / "building" / "textures"
GRADES = ("twig", "wood", "stone", "metal", "armoured")

# The materials the source models use (by their names in the GLBs): the shared Godot material, and how a cut's inside
# is mapped onto it: atlas tiles per side (0: a tiling texture, None: decals, never capped) and metres per tile.
MATS = {
    "M_PSG_Surface": (MATERIALS / "psg_surface.tres", 4, 1.0),
    "M_PSG_Markings": (MATERIALS / "psg_markings.tres", None, 1.0),
    "M_Hollow_Rock": (MATERIALS / "hollow_rock.tres", 0, 1.1),
    "M_Hollow_Glossy": (MATERIALS / "hollow_glossy.tres", 0, 1.0),
    "Door_garage": (MATERIALS / "door_garage.tres", 1, 3.0),
}
for _g in GRADES:
    MATS[f"Building_{_g}"] = (MATERIALS / f"building_{_g}.tres", 2, 4.0)

# Materials whose Godot material ignores vertex colours: their gibs carry none.
NO_COLOUR = {f"Building_{g}" for g in GRADES} | {"Door_garage"}

INSIDE_SHADE = 0.68  # a cut's colour against its island's
WELD = 1e-5  # metres: vertices this close are one


# ---------------------------------------------------------------- the materials of the building's art


def building_materials():
    """building_<grade>.tres and door_garage.tres: the kit's textures as the game draws them (GROUND.md §6.3)."""
    for g in GRADES:
        prefix = BUILDING_TEXTURES / f"building_{g}"
        write_material(f"building_{g}", f"Building_{g}", albedo=Path(f"{prefix}_BaseColor.jpg"), orm=Path(f"{prefix}_ORM.jpg"),
                       normal=Path(f"{prefix}_NormalGL.jpg"), vertex_colour=False,
                       extra=[f"normal_scale = {1.0 if g == 'twig' else 0.85:g}"])
    write_material("door_garage", "Door_garage", albedo=BUILDING_TEXTURES / "door_garage_BaseColor.jpg",
                   orm=BUILDING_TEXTURES / "door_garage_ORM.jpg", normal=BUILDING_TEXTURES / "building_metal_NormalGL.jpg",
                   vertex_colour=False, extra=["normal_scale = 0.85"])


# ---------------------------------------------------------------- the catalogue


def res_to_path(res):
    assert res.startswith("res://"), res
    return GAME / res[len("res://"):]


def gib_sets():
    """Every gib file to build: (key, gibs options, source file, parts), parts = [(part name, [source specs])]."""
    models = json.loads((DATA / "models.json").read_text(encoding="utf-8"))["models"]
    building = json.loads((DATA / "building.json").read_text(encoding="utf-8"))
    out = []
    for m in models:
        gibs = m.get("gibs")
        if not gibs:
            continue
        files = [m["file"]] if "file" in m else list(m.get("stages", []))
        if not files:
            continue
        # An outcrop breaks at its last stage, a remnant.
        source = res_to_path(files[-1])
        if m.get("kind") == "kit":
            parts = []
            for grade in building["grades"]:
                if grade.get("model") == m["key"]:
                    parts += [(p["key"], [("piece", p["key"])]) for p in building["pieces"]]
            for door in building["doors"]:
                if door.get("model") == m["key"]:
                    specs = [("door-frame", door["frameMesh"])] if door.get("frameMesh") else []
                    specs += [("door-leaf", leaf["mesh"], leaf["hinge"]) for leaf in door["leaves"]]
                    parts.append((door["key"], specs))
        else:
            parts = [("", [("model",)])]
        out.append((m["key"], gibs, source, parts))
    return out


# ---------------------------------------------------------------- sources


def import_glb(path):
    """Imports a GLB into the empty scene; returns its objects by name."""
    reset()
    bpy.ops.import_scene.gltf(filepath=str(path), merge_vertices=False)
    return {o.name: o for o in bpy.data.objects}


def triangles(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def our_lods(objects):
    """Our own models: {part: [lod0, lod1, ...]} from <Part>_lod<n> mesh objects."""
    parts = {}
    for name, o in objects.items():
        if o.type != "MESH" or "_lod" not in name:
            continue
        part, _, lod = name.rpartition("_lod")
        if lod.isdigit():
            parts.setdefault(part, {})[int(lod)] = o
    return {p: [lods[i] for i in sorted(lods)] for p, lods in parts.items()}


def kit_lods(objects, name):
    """The kits: a mesh named <name>, <name>_LOD1, <name>_LOD2."""
    lods = [objects.get(name), objects.get(name + "_LOD1"), objects.get(name + "_LOD2")]
    return [o for o in lods if o is not None and o.type == "MESH"]


def pick_lod(chains, budget):
    """The finest level at which all the chains together stay within the budget (else the coarsest)."""
    levels = max(len(c) for c in chains)
    for lod in range(levels):
        total = sum(triangles(c[min(lod, len(c) - 1)]) for c in chains)
        if total <= budget:
            return lod
    return levels - 1


def source_objects(objects, specs, budget):
    """The meshes (object, model-space matrix) that make a set's model."""
    if specs == [("model",)]:
        chains = list(our_lods(objects).values())
        lod = pick_lod(chains, budget)
        return [(c[min(lod, len(c) - 1)], c[min(lod, len(c) - 1)].matrix_world.copy()) for c in chains], lod
    chains, places = [], []
    for spec in specs:
        if spec[0] == "piece":
            chain = kit_lods(objects, spec[1])
            if not chain:
                raise RuntimeError(f"no mesh {spec[1]}")
            chains.append(chain)
            places.append(None)
        elif spec[0] == "door-frame":
            chain = kit_lods(objects, spec[1])
            if chain:
                chains.append(chain)
                places.append(None)
        elif spec[0] == "door-leaf":
            chain = kit_lods(objects, spec[1])
            if not chain:
                raise RuntimeError(f"no mesh {spec[1]}")
            hx, hy, hz = spec[2]
            # A leaf's mesh is about its hinge; the door view puts it at the hinge (Godot x, y, z = Blender x, -z, y).
            chains.append(chain)
            places.append(Matrix.Translation(Vector((hx, -hz, hy))))
    lod = pick_lod(chains, budget)
    out = []
    for chain, place in zip(chains, places):
        o = chain[min(lod, len(chain) - 1)]
        # A skinned mesh (the garage curtain) is drawn in its bind pose: the object's own transform.
        out.append((o, place if place is not None else o.matrix_world.copy()))
    return out, lod


def corner_colours(me):
    """The mesh's first colour attribute as linear RGBA per corner (white if it has none)."""
    n = len(me.loops)
    rgba = np.ones((n, 4), np.float32)
    if len(me.color_attributes):
        attr = me.color_attributes[0]
        if attr.domain == "CORNER":
            values = np.empty(n * 4, np.float32)
            attr.data.foreach_get("color", values)
            rgba = values.reshape(-1, 4)
        elif attr.domain == "POINT":
            values = np.empty(len(me.vertices) * 4, np.float32)
            attr.data.foreach_get("color", values)
            per_vertex = values.reshape(-1, 4)
            index = np.empty(n, np.int32)
            me.loops.foreach_get("vertex_index", index)
            rgba = per_vertex[index]
    return rgba


def gather(sources):
    """One bmesh in model space from (object, matrix) sources: UVs, corner colours, materials by name, welded."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    col = bm.loops.layers.float_color.new("Col")
    names = []
    for o, matrix in sources:
        me = o.data
        rgba = corner_colours(me)
        slots = []
        for m in me.materials:
            name = m.name if m else "M_PSG_Surface"
            # Blender adds .001 when the same material comes from two imports.
            name = name.split(".")[0]
            if name not in names:
                names.append(name)
            slots.append(names.index(name))
        uv_src = me.uv_layers[0].data if me.uv_layers else None
        verts = [bm.verts.new(matrix @ v.co) for v in me.vertices]
        flip = matrix.determinant() < 0
        for p in me.polygons:
            vs = [verts[i] for i in p.vertices]
            loops = list(p.loop_indices)
            if flip:
                vs.reverse()
                loops.reverse()
            try:
                f = bm.faces.new(vs)
            except ValueError:
                continue  # a doubled face in a kit level
            f.material_index = slots[p.material_index] if slots else 0
            for loop, li in zip(f.loops, loops):
                if uv_src is not None:
                    loop[uv].uv = uv_src[li].uv
                loop[col] = rgba[li]
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=WELD)
    # Faces left degenerate by the weld go.
    bmesh.ops.dissolve_degenerate(bm, dist=WELD, edges=bm.edges[:])
    return bm, names


def limit(bm, names, budget):
    """A source above the budget even at its coarsest level is decimated toward it, to half its triangles at most (thin
    parts lose their shape beyond); UVs and colours are carried."""
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    if tris <= budget * 1.1:
        return bm
    me = bpy.data.meshes.new("limit")
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("limit", me)
    bpy.context.scene.collection.objects.link(o)
    mod = o.modifiers.new("decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = max(0.5, budget / tris)
    mod.use_collapse_triangulate = True
    select_only([o])
    bpy.ops.object.modifier_apply(modifier=mod.name)
    out = bmesh.new()
    out.from_mesh(o.data)
    bpy.data.objects.remove(o)
    bpy.data.meshes.remove(me)
    bmesh.ops.remove_doubles(out, verts=out.verts[:], dist=WELD)
    bmesh.ops.dissolve_degenerate(out, dist=WELD, edges=out.edges[:])
    return out


# ---------------------------------------------------------------- islands


def islands(bm):
    """Face lists of the connected parts."""
    bm.faces.index_update()
    bm.faces.ensure_lookup_table()
    seen = set()
    out = []
    for f in bm.faces:
        if f.index in seen:
            continue
        group, stack = [], [f]
        seen.add(f.index)
        while stack:
            g = stack.pop()
            group.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        out.append(group)
    return out


def bounds(points):
    a = np.array(points, np.float64)
    return a.min(axis=0), a.max(axis=0)


def add_inner_wall(bm, faces, thickness, layer_island, island_id, col):
    """A hollow island: a copy of its surface moved in by `thickness` and turned inside out, darker."""
    dup = bmesh.ops.duplicate(bm, geom=faces)
    new_faces = [g for g in dup["geom"] if isinstance(g, bmesh.types.BMFace)]
    new_verts = [g for g in dup["geom"] if isinstance(g, bmesh.types.BMVert)]
    bm.normal_update()
    moves = [(v, v.normal.copy()) for v in new_verts]
    for v, n in moves:
        v.co -= n * thickness
    bmesh.ops.reverse_faces(bm, faces=new_faces)
    for f in new_faces:
        f[layer_island] = island_id
        for loop in f.loops:
            c = loop[col]
            loop[col] = (c[0] * 0.55, c[1] * 0.55, c[2] * 0.55, 1.0)
    return new_faces


# ---------------------------------------------------------------- seeds


def medial_points(bm, rng, count=1800):
    """Points inside the solid: under random surface points (by area), halfway to the far side."""
    bm.normal_update()
    tris = bm.calc_loop_triangles()
    if not tris:
        return np.zeros((0, 3))
    areas = np.array([max(1e-12, (t[1].vert.co - t[0].vert.co).cross(t[2].vert.co - t[0].vert.co).length * 0.5) for t in tris])
    tree = BVHTree.FromBMesh(bm, epsilon=0.0)
    picks = rng.choice(len(tris), size=count, p=areas / areas.sum())
    out = []
    for k in picks:
        t = tris[k]
        a, b = rng.random(), rng.random()
        if a + b > 1:
            a, b = 1 - a, 1 - b
        p = t[0].vert.co + (t[1].vert.co - t[0].vert.co) * a + (t[2].vert.co - t[0].vert.co) * b
        n = t[0].face.normal
        if n.length < 0.5:
            continue
        hit, _, _, d = tree.ray_cast(p - n * 1e-4, -n, 20.0)
        depth = d if hit is not None else 0.02
        out.append(p - n * (depth * 0.5))
    return np.array([tuple(p) for p in out], np.float64)


def choose_seeds(candidates, n, rng):
    """`n` seeds spread farthest-first, each picked at random among the farthest few."""
    n = min(n, len(candidates))
    first = int(rng.integers(len(candidates)))
    seeds = [candidates[first]]
    nearest = np.linalg.norm(candidates - seeds[0], axis=1)
    while len(seeds) < n:
        order = np.argsort(-nearest)
        top = order[:max(1, len(order) // 25)]
        pick = int(top[int(rng.integers(len(top)))])
        seeds.append(candidates[pick])
        nearest = np.minimum(nearest, np.linalg.norm(candidates - candidates[pick], axis=1))
    return np.array(seeds)


def cell_planes(seeds, i):
    """Planes (point, normal) bounding seed i's Voronoi cell, nearest first; the cell is where (x - p) . n <= 0."""
    planes = []
    for j in range(len(seeds)):
        if j == i:
            continue
        d = seeds[j] - seeds[i]
        length = np.linalg.norm(d)
        if length < 1e-6:
            continue
        planes.append((length, Vector(tuple((seeds[i] + seeds[j]) * 0.5)), Vector(tuple(d / length))))
    planes.sort(key=lambda x: x[0])
    return [(p, n) for _, p, n in planes]


def corners(lo, hi):
    return [Vector((x, y, z)) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]


# ---------------------------------------------------------------- cutting and capping


class Context:
    """What the cuts need to know of a set: its layers, the islands' materials, tiles, colours and planes."""

    def __init__(self, bm, names):
        self.names = names
        self.uv = bm.loops.layers.uv["UVMap"]
        self.col = bm.loops.layers.float_color["Col"]
        self.island = bm.faces.layers.int.get("island") or bm.faces.layers.int.new("island")
        self.cap = bm.faces.layers.int.get("cap") or bm.faces.layers.int.new("cap")
        self.material = {}  # island -> material index
        self.window = {}  # island -> (u0, v0, size, metres) or (None, metres) for tiling, or None: never capped
        self.colour = {}  # island -> inside colour
        self.offset = {}  # island -> how far its caps sit inside the cut (no two islands' caps in one plane)


def describe_islands(ctx, groups):
    """Each island's material, cap window and inside colour."""
    for index, faces in enumerate(groups):
        area = {}
        for f in faces:
            area[f.material_index] = area.get(f.material_index, 0.0) + f.calc_area()
        mat = max(area, key=area.get) if area else 0
        ctx.material[index] = mat
        name = ctx.names[mat] if mat < len(ctx.names) else "M_PSG_Surface"
        _, tiles, metres = MATS.get(name, (None, 0, 1.0))
        if tiles is None:
            ctx.window[index] = None
        elif tiles == 0:
            ctx.window[index] = (None, metres)
        else:
            weight = {}
            for f in faces:
                if f.material_index != mat:
                    continue
                u = sum(loop[ctx.uv].uv.x for loop in f.loops) / len(f.loops)
                v = sum(loop[ctx.uv].uv.y for loop in f.loops) / len(f.loops)
                tile = (min(tiles - 1, max(0, int(math.floor(u * tiles)))), min(tiles - 1, max(0, int(math.floor(v * tiles)))))
                weight[tile] = weight.get(tile, 0.0) + f.calc_area()
            tx, ty = max(weight, key=weight.get) if weight else (0, 0)
            size = 1.0 / tiles
            ctx.window[index] = ((tx + 0.03) * size, (ty + 0.03) * size, size * 0.94, metres)
        total = np.zeros(3)
        count = 0
        for f in faces:
            for loop in f.loops:
                c = loop[ctx.col]
                total += (c[0], c[1], c[2])
                count += 1
        mean = total / max(1, count)
        ctx.colour[index] = (*(mean * INSIDE_SHADE), 1.0)
        # Islands that overlap would cap in the same plane; each sits a fraction of a millimetre further in.
        ctx.offset[index] = 0.00015 * (index % 5)
        for f in faces:
            f[ctx.island] = index


def plane_basis(n):
    a = Vector((1, 0, 0)) if abs(n.x) < 0.8 else Vector((0, 1, 0))
    u = n.cross(a).normalized()
    return u, n.cross(u)


def cap(bm, ctx, cut_edges, co, no):
    """Fills the closed loops of a cut, island by island, with inside faces facing +no."""
    by_island = {}
    for e in cut_edges:
        if not e.is_valid:
            continue
        faces = e.link_faces
        if not faces:
            continue
        by_island.setdefault(faces[0][ctx.island], []).append(e)
    for island, edges in sorted(by_island.items()):
        if ctx.window.get(island) is None:
            continue
        # Only closed loops are filled: every vertex on two cut edges of this island.
        degree = {}
        for e in edges:
            for v in e.verts:
                degree[v] = degree.get(v, 0) + 1
        closed = [e for e in edges if all(degree[v] == 2 for v in e.verts)]
        if len(closed) < 3:
            continue
        try:
            result = bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=closed, normal=no)
        except Exception:  # noqa: BLE001 - a fill that fails leaves the cut open
            continue
        faces = [g for g in result["geom"] if isinstance(g, bmesh.types.BMFace)]
        if not faces:
            continue
        for f in faces:
            f.normal_update()
            if f.normal.dot(no) < 0:
                f.normal_flip()
        dress_cap(ctx, island, faces, no)


def dress_cap(ctx, island, faces, no):
    """A cap's material, colour and UVs: planar in its plane, inside the island's atlas tile."""
    u_axis, v_axis = plane_basis(no)
    window = ctx.window[island]
    points = {v: (v.co.dot(u_axis), v.co.dot(v_axis)) for f in faces for v in f.verts}
    us = [p[0] for p in points.values()]
    vs = [p[1] for p in points.values()]
    u0, v0 = min(us), min(vs)
    extent = max(max(us) - u0, max(vs) - v0, 1e-6)
    for f in faces:
        f.material_index = ctx.material[island]
        f[ctx.island] = island
        f[ctx.cap] = 1
        f.smooth = False
        for loop in f.loops:
            pu, pv = points[loop.vert]
            if window[0] is None:
                metres = window[1]
                loop[ctx.uv].uv = (pu / metres, pv / metres)
            else:
                wu, wv, size, metres = window
                scale = size / metres
                if extent * scale > size:
                    scale = size / extent
                loop[ctx.uv].uv = (wu + (pu - u0) * scale, wv + (pv - v0) * scale)
            loop[ctx.col] = ctx.colour[island]
    # Pushed in a hair: overlapping islands' caps never fight in one plane.
    d = ctx.offset[island]
    if d > 0:
        for v in {v for f in faces for v in f.verts}:
            v.co -= no * d


def cut(bm, ctx, planes):
    """Keeps what is inside every plane, capping each cut."""
    for co, no in planes:
        if not bm.faces:
            return
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        result = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=co, plane_no=no, clear_outer=True, clear_inner=False)
        edges = [g for g in result["geom_cut"] if isinstance(g, bmesh.types.BMEdge)]
        if edges:
            cap(bm, ctx, edges, co, no)


def subset(bm, keep_islands, ctx):
    """A copy of `bm` holding only the faces of the given islands."""
    piece = bm.copy()
    layer = piece.faces.layers.int["island"]
    doomed = [f for f in piece.faces if f[layer] not in keep_islands]
    if doomed:
        bmesh.ops.delete(piece, geom=doomed, context="FACES")
    return piece


def append(into, piece):
    """Adds a piece's geometry to a chunk's bmesh (layers matched by name)."""
    me = bpy.data.meshes.new("tmp")
    piece.to_mesh(me)
    into.from_mesh(me)
    bpy.data.meshes.remove(me)


# ---------------------------------------------------------------- fracturing


def fracture(bm, names, count, rng, hollow=0.0, cut_islands=True):
    """Cuts a model's bmesh into pieces; returns a list of bmeshes (model space)."""
    ctx = Context(bm, names)
    groups = islands(bm)
    describe_islands(ctx, groups)
    lo, hi = bounds([v.co for v in bm.verts])
    diag = float(np.linalg.norm(hi - lo))
    info = []
    for index, faces in enumerate(groups):
        pts = [v.co for f in faces for v in f.verts]
        ilo, ihi = bounds(pts)
        info.append((ilo, ihi, float(np.linalg.norm(ihi - ilo))))
    if hollow > 0:
        for index, faces in enumerate(groups):
            if info[index][2] > 0.4 * diag and all(len(e.link_faces) == 2 for f in faces for e in f.edges):
                add_inner_wall(bm, faces, hollow, ctx.island, index, ctx.col)
    candidates = medial_points(bm, rng)
    seeds = choose_seeds(candidates, count, rng)
    cell = diag / max(1.0, len(seeds) ** (1 / 3))
    small = max(0.06, 0.5 * cell) if cut_islands else float("inf")
    whole = {i: [] for i in range(len(seeds))}
    big = []
    for index, (ilo, ihi, d) in enumerate(info):
        if d < small:
            middle = (ilo + ihi) * 0.5
            owner = int(np.argmin(np.linalg.norm(seeds - middle, axis=1)))
            whole[owner].append(index)
        else:
            big.append(index)
    chunks = []
    for i in range(len(seeds)):
        planes = cell_planes(seeds, i)
        chunk = bmesh.new()
        chunk.loops.layers.uv.new("UVMap")
        chunk.loops.layers.float_color.new("Col")
        chunk.faces.layers.int.new("island")
        chunk.faces.layers.int.new("cap")
        touching, relevant = [], []
        for index in big:
            ilo, ihi, _ = info[index]
            box = corners(ilo, ihi)
            inside = True
            for co, no in planes:
                sides = [(c - co).dot(no) for c in box]
                if min(sides) > 1e-6:
                    inside = False
                    break
            if inside:
                touching.append(index)
        if touching:
            tlo = np.min([info[k][0] for k in touching], axis=0)
            thi = np.max([info[k][1] for k in touching], axis=0)
            box = corners(tlo, thi)
            for co, no in planes:
                sides = [(c - co).dot(no) for c in box]
                if max(sides) > 1e-6:
                    relevant.append((co, no))
            piece = subset(bm, set(touching), ctx)
            ctx_piece = Context(piece, names)
            ctx_piece.material, ctx_piece.window, ctx_piece.colour, ctx_piece.offset = ctx.material, ctx.window, ctx.colour, ctx.offset
            cut(piece, ctx_piece, relevant)
            if piece.faces:
                append(chunk, piece)
            piece.free()
        if whole[i]:
            piece = subset(bm, set(whole[i]), ctx)
            append(chunk, piece)
            piece.free()
        if chunk.faces:
            chunks.append(chunk)
        else:
            chunk.free()
    return merge_slivers(chunks, diag)


def extent(chunk):
    lo, hi = bounds([v.co for v in chunk.verts])
    return lo, hi, float(np.linalg.norm(hi - lo))


def merge_slivers(chunks, diag):
    """Pieces much smaller than the rest join their nearest neighbour."""
    while len(chunks) > 2:
        sizes = [extent(c) for c in chunks]
        median = float(np.median([s[2] for s in sizes]))
        worst = min(range(len(chunks)), key=lambda k: sizes[k][2])
        if sizes[worst][2] >= 0.3 * median and len(chunks[worst].faces) >= 6:
            break
        middle = (sizes[worst][0] + sizes[worst][1]) * 0.5
        others = [k for k in range(len(chunks)) if k != worst]
        target = min(others, key=lambda k: np.linalg.norm((sizes[k][0] + sizes[k][1]) * 0.5 - middle))
        append(chunks[target], chunks[worst])
        chunks[worst].free()
        del chunks[worst]
    return chunks


# ---------------------------------------------------------------- pieces as objects


def principal_frame(points):
    """A rotation whose axes are the points' principal axes (longest first), signs fixed, right-handed."""
    a = np.array(points, np.float64)
    centred = a - a.mean(axis=0)
    if len(a) < 4:
        return Matrix.Identity(3)
    values, vectors = np.linalg.eigh(np.cov(centred.T))
    axes = vectors[:, np.argsort(-values)]
    for k in range(3):
        column = axes[:, k]
        if column[np.argmax(np.abs(column))] < 0:
            axes[:, k] = -column
    if np.linalg.det(axes) < 0:
        axes[:, 2] = -axes[:, 2]
    return Matrix([list(axes[r]) for r in range(3)])


def chunk_object(chunk, name, names, colour, target):
    """A piece as an object: its mesh about its own box's middle, along its principal axes, placed where it sat."""
    rot = principal_frame([tuple(v.co) for v in chunk.verts])
    inv = rot.transposed()
    local = [inv @ v.co for v in chunk.verts]
    lo, hi = bounds(local)
    middle = Vector(tuple((lo + hi) * 0.5))
    centre = rot @ middle
    for v, p in zip(chunk.verts, local):
        v.co = p - middle
    # A fixed face order, each face from its lowest vertex: every build writes the same file.
    chunk.verts.index_update()
    me = bpy.data.meshes.new(name)
    chunk.to_mesh(me)
    order = sorted(range(len(me.polygons)), key=lambda k: canonical_key(me, k))
    me = reorder(me, order, name)
    o = bpy.data.objects.new(name, me)
    target.objects.link(o)
    for n in names:
        mat = bpy.data.materials.get(n) or bpy.data.materials.new(n)
        me.materials.append(mat)
    o.matrix_world = Matrix.Translation(centre) @ rot.to_4x4()
    # Shading: the model's faces smooth with its sharp edges, the cuts flat.
    cap_attr = me.attributes.get("cap")
    caps = np.zeros(len(me.polygons), np.int32)
    if cap_attr is not None:
        cap_attr.data.foreach_get("value", caps)
    for p, c in zip(me.polygons, caps):
        p.use_smooth = not c
    me.set_sharp_from_angle(angle=math.radians(38))
    me.validate(verbose=False, clean_customdata=False)
    for a in ("island", "cap"):
        if me.attributes.get(a) is not None:
            me.attributes.remove(me.attributes[a])
    if colour and me.color_attributes.get("Col") is not None:
        me.color_attributes.active_color = me.color_attributes["Col"]
        me.color_attributes.render_color_index = me.color_attributes.find("Col")
    elif me.color_attributes.get("Col") is not None:
        me.color_attributes.remove(me.color_attributes["Col"])
    return o


def canonical_key(me, k):
    p = me.polygons[k]
    cos = [tuple(round(c, 6) for c in me.vertices[i].co) for i in p.vertices]
    start = cos.index(min(cos))
    return tuple(cos[start:] + cos[:start])


def reorder(me, order, name):
    """The same mesh with its vertices by position and its faces in `order` (UVs, colours, materials, attributes kept)."""
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    by_place = sorted(range(len(bm.verts)), key=lambda k: (tuple(round(c, 6) for c in bm.verts[k].co), tuple(bm.verts[k].co)))
    for new, old in enumerate(by_place):
        bm.verts[old].index = new
    bm.verts.sort()
    bm.faces.ensure_lookup_table()
    for new, old in enumerate(order):
        bm.faces[old].index = new
    bm.faces.sort()
    bpy.data.meshes.remove(me)
    out = bpy.data.meshes.new(name)
    bm.to_mesh(out)
    bm.free()
    return out


# ---------------------------------------------------------------- one set


def default_count(lo, hi):
    sides = sorted(hi - lo, reverse=True)
    return int(min(12, max(6, round(1.5 + 1.6 * (sides[0] + sides[1])))))


def build_set(key, gibs, source, parts):
    objects = import_glb(source)
    budget = int(gibs.get("budget", 1200))
    if BUDGET_OVERRIDE:
        budget = BUDGET_OVERRIDE
    target_path = res_to_path(gibs["file"])
    pieces = []  # (name, bmesh, part)
    names_all = []
    lines = []
    for part, specs in parts:
        sources, lod = source_objects(objects, specs, budget)
        bm, names = gather(sources)
        bm = limit(bm, names, budget)
        for n in names:
            if n not in MATS:
                raise RuntimeError(f"{key}: no Godot material for {n}")
        lo, hi = bounds([v.co for v in bm.verts])
        count = int(gibs.get("pieces", default_count(lo, hi)))
        rng = np.random.default_rng(zlib.crc32(f"{key}/{part}".encode()))
        tris_before = sum(len(f.verts) - 2 for f in bm.faces)
        chunks = fracture(bm, names, count, rng, float(gibs.get("hollow", 0.0)), bool(gibs.get("cut", True)))
        bm.free()
        # Name pieces in a fixed order: by height, then across.
        order = sorted(chunks, key=lambda c: tuple(np.round(((extent(c)[0] + extent(c)[1]) * 0.5)[[2, 0, 1]], 4)))
        tris_after = 0
        for k, chunk in enumerate(order):
            for f in chunk.faces:
                tris_after += len(f.verts) - 2
            pieces.append((f"{part}_c{k:02d}" if part else f"c{k:02d}", chunk, part, names))
        for n in names:
            if n not in names_all:
                names_all.append(n)
        caps = sum(1 for c in order for f in c.faces if f[c.faces.layers.int["cap"]] == 1)
        lines.append(f"{key}{'/' + part if part else ''}: LOD{lod} {tris_before} triangles -> {len(order)} pieces, {tris_after} triangles ({caps} cap faces)")
    # The scene again, empty, for the pieces alone.
    reset()
    out = collection("HOLLOW_GIBS")
    colour = not all(n in NO_COLOUR for n in names_all)
    exported = []
    groups = {}
    for name, chunk, part, names in pieces:
        o = chunk_object(chunk, name, names, colour, out)
        chunk.free()
        exported.append(o)
        if part:
            if part not in groups:
                g = bpy.data.objects.new(part, None)
                out.objects.link(g)
                groups[part] = g
                exported.append(g)
            world = o.matrix_world.copy()
            o.parent = groups[part]
            o.matrix_world = world
    export(target_path, exported, names_all, colour)
    for line in lines:
        log(line)


def export(path, objects, names, colour):
    path.parent.mkdir(parents=True, exist_ok=True)
    select_only(objects)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_image_format="NONE",
        export_materials="EXPORT", export_vertex_color="ACTIVE" if colour else "NONE", export_all_vertex_colors=False,
        export_yup=True, export_apply=False, export_texcoords=True, export_normals=True, export_tangents=False,
        export_animations=False, export_extras=False, export_cameras=False, export_lights=False)
    write_scene_import(path, {n: MATS[n][0] for n in names})
    meshes = [o for o in objects if o.type == "MESH"]
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
    log(f"exported {path.name}: {len(meshes)} pieces, {tris} triangles, {path.stat().st_size / 1e6:.2f} MB")


# ---------------------------------------------------------------- main


BUDGET_OVERRIDE = 0


def main():
    global BUDGET_OVERRIDE
    args = script_args()
    if "--budget" in args:
        BUDGET_OVERRIDE = int(args[args.index("--budget") + 1])
        args = [a for i, a in enumerate(args) if a != "--budget" and (i == 0 or args[i - 1] != "--budget")]
    listing = "--list" in args
    wanted = [a for a in args if not a.startswith("--")]
    sets = gib_sets()
    if not listing and not wanted:
        building_materials()
    elif not listing and any(k.startswith(("building-", "doors-")) for k in wanted):
        building_materials()
    built = set()
    for key, gibs, source, parts in sets:
        if wanted and key not in wanted:
            continue
        # Two entries may share a model and its gibs (the research table stands in as the engineering workbench).
        if gibs["file"] in built:
            continue
        built.add(gibs["file"])
        if listing:
            log(f"{key}: {source.name} -> {gibs['file']} ({len(parts)} part{'s' if len(parts) != 1 else ''}, {gibs.get('material')})")
            continue
        log(f"=== {key}")
        build_set(key, gibs, source, parts)


main()
