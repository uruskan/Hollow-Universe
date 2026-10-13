"""PSG_BuildingKit_R01 -> game/assets/building/building_<grade>.glb (GROUND.md §6.3, §9).

    blender --background --factory-startup --python art/import/building_kit.py [-- twig wood ...]

One GLB per grade. Each holds, under the game's piece keys:
  <key>, <key>_LOD1, <key>_LOD2            the kit's three levels of detail
  <key>_col00-convcolonly ...               the kit's convex hulls, named for Godot's importer
for the 13 kit families, and the 7 pieces the kit lacks (low wall, L and U stairs, ramp, steps, square and triangle
roofs), built here with the kit's own generator (its MeshBuilder, tier surfaces, atlas tiles and the twig revision's
TwigMesh), so they share its look and texel density. One material per grade from the kit's atlas, shrunk to 2K and
stored as JPEG: base colour, OpenGL normal map, ORM. Vertices in metres; Godot's axes come from the glTF exporter.
"""
import importlib.util
import math
import pathlib
import sys

import bpy
import bmesh
from mathutils import Vector

# No __pycache__ beside the scripts in the repository.
sys.dont_write_bytecode = True
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import kit_common as kc  # noqa: E402

KIT = pathlib.Path(r"D:\GameDev\Assets\PSG_BuildingKit_R01")
OUT = kc.REPO / "game" / "assets" / "building"
TIERS = {"twig": "Twig", "wood": "Wood", "stone": "Stone", "metal": "Metal", "armoured": "Armored"}

# The kit's families and the game's piece keys (game/data/ground/building.json).
KIT_PIECES = {
    "FoundationSquare": "foundation",
    "FoundationTriangle": "foundation-triangle",
    "FloorSquare": "floor",
    "FloorTriangle": "floor-triangle",
    "Wall": "wall",
    "HalfWall": "half-wall",
    "WindowWall": "window",
    "DoorwaySingle": "doorway",
    "DoorwayDouble": "wall-frame",
    "FloorFrameSquare": "floor-frame",
    "FloorFrameTriangle": "floor-frame-triangle",
    "StairsSquare": "stairs-spiral",
    "StairsTriangle": "stairs-spiral-triangle",
}


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, str(path))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


g = load_module("psg_basekit", KIT / "Source" / "make_buildings.py")
tw = load_module("psg_twig_r02", KIT / "Source" / "twig_revision_r02.py")
ROOT3 = math.sqrt(3)


# ---------------------------------------------------------------- the pieces the kit lacks
# Built in Blender's axes (x along a wall, -Y outside, +Z up); the game's (x, y, z) is Blender's (x, z, -y).

def builder(tier, seed, quality):
    return tw.TwigMesh(seed, quality) if tier == "Twig" else g.MeshBuilder(seed)


def trim_tile(tier):
    return 1 if tier in ("Twig", "Armored") else (2 if tier == "Metal" else 0)


def low_wall(tier, seed, quality):
    """A 1 m wall: make_wall's surfaces and trims (twig: the R02 twig wall's) at a third of the height."""
    h = 1.0
    if tier == "Twig":
        m = tw.TwigMesh(seed, quality)
        tw.region(m, -1.5, 1.5, 0, h)
        for x in (-1.43, 1.43):
            m.branch((x, 0, .03), (x, 0, h - .03), .062, 1, .022)
        m.branch((-1.46, 0, h - .052), (1.46, 0, h - .052), .052, 1, .006)
        m.branch((-1.46, 0, .054), (1.46, 0, .054), .053, 1, .006)
        m.branch((-1.40, -.058, .12), (1.40, -.058, h - .12), .039, 1, .030)
        for x in (-1.43, 1.43):
            for z in (.12, h - .12):
                m.rope_wrap((x, 0, z), .078, turns=7)
        return m
    m = g.MeshBuilder(seed)
    g.wall_region(m, tier, -1.5, 1.5, 0, h)
    if tier != "Stone":
        mt = 0 if tier == "Wood" else (2 if tier == "Metal" else 1)
        for x in (-1.43, 1.43):
            m.box((x, .005, h / 2), (.14, .28, h), mt)
        m.box((0, .006, h - .07), (3, .28, .14), mt)
        m.box((0, .006, .07), (3, .28, .14), mt)
    return m


def deck(tier, seed, quality, poly):
    """A slab's surface over a polygon centred on its origin, its top at z = 0 (the kit's floor deck and rim)."""
    m = builder(tier, seed, quality)
    if tier == "Twig":
        tw.ring(m, poly)
        tw.deck(m, poly)
    else:
        g.deck_fill(m, poly, tier)
        g.perimeter(m, poly, -.08, .12, .16, tier)
    return m


def tread(tier, seed, quality, x0, x1, y0, y1):
    """One stair tread over a rectangle, its top at z = 0."""
    m = builder(tier, seed, quality)
    if tier == "Twig":
        w = x1 - x0
        for f in (.14, .38, .62, .86):
            y = y0 + (y1 - y0) * f
            m.branch((x0 + .03, y, -.025), (x1 - .03, y, -.025), .022, 0, .004)
        for x in (x0 + .12, x1 - .12):
            m.rope_wrap((x, (y0 + y1) / 2, -.03), .05, axis=(0, 1, 0), turns=3)
        if w > .8:
            m.branch((x0 + .05, y0 + .03, -.05), (x1 - .05, y1 - .03, -.05), .012, 0, .003)
    else:
        poly = g.rect(x0, x1, y0, y1)
        if tier == "Wood":
            m.prism(poly, -.07, -.03, 1)
            for a, b in ((x0, (x0 + x1) / 2), ((x0 + x1) / 2, x1)):
                m.prism(g.rect(a + .004, b - .004, y0 + .003, y1 - .003), -.045, 0, 0)
        elif tier == "Stone":
            m.prism(g.rect(x0 + .01, x1 - .01, y0, y1), -.10, -.02, 1)
            x = x0
            while x < x1 - .05:
                nx = min(x1, x + m.rng.uniform(.38, .62))
                if x1 - nx < .14:
                    nx = x1
                m.prism(g.rect(x + .006, nx - .006, y0 + .006, y1 - .006), -.05, 0, 0)
                x = nx
        else:
            m.prism(poly, -.06, -.025, 2)
            m.prism(g.rect(x0 + .004, x1 - .004, y0 + .004, y1 - .004), -.03, 0, 0)
            for x in (x0 + .07, x1 - .07):
                m.bolt((x, (y0 + y1) / 2, -.002), (0, 0, 1), .011 if tier == "Metal" else .014, 2 if tier == "Metal" else 1)
    return m


def merge(m, part, origin, basis=((1, 0, 0), (0, 1, 0), (0, 0, 1))):
    m.transformed_merge(part, basis, origin)


def beam(m, tier, a, b, width=.10, depth=.12):
    if tier == "Twig":
        m.branch(a, b, width * .45, 1, .01)
    else:
        m.beam(a, b, width, depth, trim_tile(tier))


def post(m, tier, x, y, z0, z1):
    if tier == "Twig":
        m.branch((x, y, z0), (x, y, z1), .05, 1, .01)
        m.rope_wrap((x, y, z1 - .12), .07, turns=4)
    else:
        m.box((x, y, (z0 + z1) / 2), (.12, .12, z1 - z0), trim_tile(tier))


def flight(m, tier, seed, quality, steps, start, end, x0, x1, z0, z1, along_y):
    """A straight flight: `steps` treads from `start` to `end` along y (or x), between x0..x1 (or y), rising z0 to z1,
    each tread's middle on the slope the collision follows; two stringers under its sides."""
    run = (end - start) / steps
    rise = (z1 - z0) / steps
    for i in range(steps):
        a, b = start + i * run, start + (i + 1) * run
        lo, hi = min(a, b), max(a, b)
        top = z0 + (i + .5) * rise
        if along_y:
            merge(m, tread(tier, seed + 17 * i, quality, x0, x1, lo, hi), (0, 0, top))
        else:
            # Treads are built long along x; turned a quarter (x -> y, y -> -x) for a flight that runs along x.
            part = tread(tier, seed + 17 * i, quality, x0, x1, -hi, -lo)
            merge(m, part, (0, 0, top), ((0, 1, 0), (-1, 0, 0), (0, 0, 1)))
    for side in (x0 + .05, x1 - .05):
        if along_y:
            beam(m, tier, (side, start, z0 - .06), (side, end, z1 - .06))
        else:
            beam(m, tier, (start, side, z0 - .06), (end, side, z1 - .06))


def stairs_u(tier, seed, quality):
    """hollow-orbit's U stairs in the kit's style: up the east half, a landing across the far side, up the west half."""
    m = builder(tier, seed, quality)
    # Game z = +1.5 is Blender y = -1.5.
    flight(m, tier, seed, quality, 8, -1.5, .5, 0, 1.5, 0, 1.5, along_y=True)
    merge(m, landing(tier, seed + 5, quality, -1.5, 1.5, .5, 1.5), (0, 0, 1.5))
    flight(m, tier, seed + 101, quality, 8, .5, -1.5, -1.5, 0, 1.5, 3.0, along_y=True)
    for x, y in ((-1.4, 1.4), (1.4, 1.4), (-1.4, .6), (1.4, .6)):
        post(m, tier, x, y, 0, 1.45)
    post(m, tier, -.75, -1.4, 0, 2.9)
    return m


def stairs_l(tier, seed, quality):
    """L stairs: up the east strip, a landing in the far east corner, then west along the far strip."""
    m = builder(tier, seed, quality)
    flight(m, tier, seed, quality, 8, -1.5, .3, .3, 1.5, 0, 1.5, along_y=True)
    merge(m, landing(tier, seed + 5, quality, .3, 1.5, .3, 1.5), (0, 0, 1.5))
    flight(m, tier, seed + 101, quality, 8, .3, -1.5, .3, 1.5, 1.5, 3.0, along_y=False)
    for x, y in ((.4, .4), (1.4, .4), (1.4, 1.4), (.4, 1.4)):
        post(m, tier, x, y, 0, 1.45)
    post(m, tier, -1.4, .9, 0, 2.9)
    return m


def landing(tier, seed, quality, x0, x1, y0, y1):
    """A landing's deck over a rectangle, centred for the kit's deck functions, its top at z = 0."""
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    poly = g.rect(x0 - cx, x1 - cx, y0 - cy, y1 - cy)
    part = deck(tier, seed, quality, poly)
    m = builder(tier, seed, quality)
    merge(m, part, (cx, cy, 0))
    return m


def sloped(tier, seed, quality, poly, origin, slope):
    """A slab's deck laid on a slope: `slope` is the unit direction up it in Blender's x = 0 plane."""
    u = Vector((1, 0, 0))
    v = Vector(slope).normalized()
    n = u.cross(v)
    m = builder(tier, seed, quality)
    merge(m, deck(tier, seed, quality, poly), origin, (tuple(u), tuple(v), tuple(n)))
    return m


def gable(m, tier, a, b, ha, hb, thickness=.06):
    """An upright triangle under a roof's slanting side, from edge point a (height ha) to b (height hb), one of them 0,
    a thin panel just inside the footprint (the same panel PieceShapes.Gables gives the collision)."""
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    u = (b - a).normalized()
    length = (b - a).length
    # Inside is to the left of a -> b (the footprint runs counter-clockwise seen from above).
    n = Vector((-u.y, u.x, 0))
    poly = [(0, 0), (length, 0), (length, hb)] if hb > ha else [(0, 0), (length, 0), (0, ha)]
    tile = 0 if tier in ("Twig", "Metal", "Armored") else 1
    m.prism(poly, 0, thickness, tile, (tuple(u), (0, 0, 1), tuple(n)), tuple(a))
    if tier == "Twig":
        for f in (.3, .6):
            x = length * f if hb > ha else length * (1 - f)
            top = (hb if hb > ha else ha) * (x / length if hb > ha else 1 - x / length)
            p0 = a + u * x + n * .03
            m.branch(tuple(p0), tuple(p0 + Vector((0, 0, max(.05, top - .05)))), .03, 1, .01)


def roof(tier, seed, quality):
    """A square roof: a 20 cm deck whose underside rises from its low edge (game +Z) 3 m up across 3 m."""
    length = math.hypot(3, 3)
    v = Vector((0, 3, 3)).normalized()
    n = Vector((1, 0, 0)).cross(v)
    top_centre = Vector((0, 0, 1.5)) + n * .2
    m = sloped(tier, seed, quality, g.rect(-1.5, 1.5, -length / 2, length / 2), tuple(top_centre), tuple(v))
    # Eave and ridge beams under the deck's edges; gables close the sides.
    beam(m, tier, (-1.5, -1.5, -.06), (1.5, -1.5, -.06), .12, .12)
    beam(m, tier, (-1.5, 1.5, 2.94), (1.5, 1.5, 2.94), .12, .12)
    gable(m, tier, (1.5, -1.5), (1.5, 1.5), 0, 3)
    gable(m, tier, (-1.5, 1.5), (-1.5, -1.5), 3, 0)
    return m


def roof_triangle(tier, seed, quality):
    """A triangle roof: its underside rises from the base edge (game +Z) to the apex, 3 m up."""
    altitude = 1.5 * ROOT3
    base = Vector((0, -altitude / 3, 0))
    apex = Vector((0, 2 * altitude / 3, 3))
    v = (apex - base).normalized()
    slant = (apex - base).length
    n = Vector((1, 0, 0)).cross(v)
    poly = [(-1.5, -slant / 3), (1.5, -slant / 3), (0, 2 * slant / 3)]
    top_centre = base + v * (slant / 3) + n * .2
    m = sloped(tier, seed, quality, poly, tuple(top_centre), tuple(v))
    beam(m, tier, (-1.5, -altitude / 3, -.06), (1.5, -altitude / 3, -.06), .12, .12)
    gable(m, tier, (1.5, -altitude / 3), (0, 2 * altitude / 3), 0, 3)
    gable(m, tier, (0, 2 * altitude / 3), (-1.5, -altitude / 3), 3, 0)
    return m


def ramp(tier, seed, quality):
    """A quarter-storey ramp: a deck on the slope (its top is the walking surface) over a solid body."""
    rise = .75
    length = math.hypot(3, rise)
    v = Vector((0, 3, rise)).normalized()
    m = sloped(tier, seed, quality, g.rect(-1.5, 1.5, -length / 2, length / 2), (0, 0, rise / 2), tuple(v))
    body(m, tier, [(-1.5, 0), (1.5, 0), (1.5, rise - .16), (-1.5, .04)])
    return m


def steps(tier, seed, quality):
    """Half-storey steps: six 25 cm steps across the square, each tread's middle on the collision's slope."""
    m = builder(tier, seed, quality)
    count, height = 6, 1.5
    run, rise = 3 / count, height / count
    for i in range(count):
        y0, y1 = -1.5 + i * run, -1.5 + (i + 1) * run
        top = (i + .5) * rise
        merge(m, tread(tier, seed + 13 * i, quality, -1.5, 1.5, y0, y1), (0, 0, top))
        body(m, tier, [(y0, 0), (y1, 0), (y1, top - .05), (y0, top - .05)])
    return m


def body(m, tier, profile):
    """A solid body under a ramp or a step: a profile in Blender's (y, z) pushed along x, in the tier's structure tile."""
    if tier == "Twig":
        (y0, _), (y1, _) = profile[0], profile[1]
        for x in (-1.4, 0, 1.4):
            m.branch((x, y0 + .05, .02), (x, y1 - .05, .02), .045, 1, .01)
        return
    tile = 1 if tier in ("Wood", "Stone") else 2
    m.prism(profile, -1.45, 1.45, tile, ((0, 1, 0), (0, 0, 1), (1, 0, 0)))


GENERATED = {
    "low-wall": low_wall,
    "stairs-l": stairs_l,
    "stairs-u": stairs_u,
    "ramp": ramp,
    "steps": steps,
    "roof": roof,
    "roof-triangle": roof_triangle,
}


# ---------------------------------------------------------------- their collision: the same hulls as PieceShapes.cs

def game_to_blender(p):
    x, y, z = p
    return (x, -z, y)


def hull_object(name, points_game):
    """A convex hull object from points in the game's axes."""
    bm = bmesh.new()
    for p in points_game:
        bm.verts.new(game_to_blender(p))
    bmesh.ops.convex_hull(bm, input=list(bm.verts))
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(o)
    return o


def box(x0, x1, y0, y1, z0, z1):
    return [(x, y, z) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)]


def wedge(x0, x1, z_low, z_high, y0, y1):
    return [(x0, y0, z_low), (x1, y0, z_low), (x0, y0, z_high), (x1, y0, z_high), (x0, y1, z_high), (x1, y1, z_high)]


def sloped_slab(a0, a1, b0, b1, y0, y1, thickness, along_z):
    def p(a, b, y):
        return (a, y, b) if along_z else (b, y, a)
    return [p(a0, b0, y0), p(a1, b0, y0), p(a0, b1, y1), p(a1, b1, y1),
            p(a0, b0, y0 - thickness), p(a1, b0, y0 - thickness), p(a0, b1, y1 - thickness), p(a1, b1, y1 - thickness)]


def gable_hulls(polygon, low, span, square, height=3.0, thickness=.06):
    """PieceShapes.Gables: the panels under a roof's slanting sides."""
    hulls = []
    for i in range(1, len(polygon)):
        if square and i == 2:
            continue
        (ax, az), (bx, bz) = polygon[i], polygon[(i + 1) % len(polygon)]
        length = math.hypot(bx - ax, bz - az)
        nx, nz = (bz - az) / length, -(bx - ax) / length
        points = []
        for x, z in ((ax, az), (bx, bz)):
            for inset in (0.0, thickness):
                px, pz = x + nx * inset, z + nz * inset
                points.append((px, 0, pz))
                rise = height * (low - z) / span
                if rise > 1e-6:
                    points.append((px, rise, pz))
        hulls.append(points)
    return hulls


def roof_hull(polygon, low, span, height=3.0, thickness=.2):
    n = Vector((0, span, height)).normalized() * thickness
    points = []
    for x, z in polygon:
        bottom = (x, height * (low - z) / span, z)
        points.append(bottom)
        points.append((bottom[0] + n.x, bottom[1] + n.y, bottom[2] + n.z))
    return points


def generated_hulls(key):
    """The convex parts PieceShapes.For gives these pieces (metres, the game's axes)."""
    half = 1.5
    if key == "low-wall":
        return [box(-half, half, 0, 1.0, -.15, .15)]
    if key == "ramp":
        return [wedge(-half, half, half, -half, 0, .75)]
    if key == "steps":
        return [wedge(-half, half, half, -half, 0, 1.5)]
    if key == "stairs-u":
        return [wedge(0, half, half, -.5, 0, 1.5), box(-half, half, 0, 1.5, -half, -.5),
                sloped_slab(-half, 0, -.5, half, 1.5, 3.0, .25, True)]
    if key == "stairs-l":
        return [wedge(.3, half, half, -.3, 0, 1.5), box(.3, half, 0, 1.5, -half, -.3),
                sloped_slab(-half, -.3, .3, -half, 1.5, 3.0, .25, False)]
    if key == "roof":
        square = [(-half, half), (half, half), (half, -half), (-half, -half)]
        return [roof_hull(square, half, 3.0)] + gable_hulls(square, half, 3.0, True)
    if key == "roof-triangle":
        altitude = 1.5 * ROOT3
        triangle = [(-half, altitude / 3), (half, altitude / 3), (0, -2 * altitude / 3)]
        return [roof_hull(triangle, altitude / 3, altitude)] + gable_hulls(triangle, altitude / 3, altitude, False)
    raise KeyError(key)


# ---------------------------------------------------------------- per grade

def lod_object(key, tier, seed, level):
    """A generated piece at a level of detail: the kit's bevelled build, the same without bevels, then 45 % of that.
    Twig follows its revision: three authored qualities."""
    quality = level if tier == "Twig" else 0
    m = GENERATED[key](tier, seed, quality)
    name = key if level == 0 else f"{key}_LOD{level}"
    o = m.mesh(name, bpy.context.scene.collection)
    o["piece_kind"] = key
    if tier == "Twig":
        g.select_only([o])
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(o.data)
        bm.free()
    else:
        g.prepare_mesh(o, tier, lightmap=False, bevel_geometry=level == 0)
        if level == 2:
            g.select_only([o])
            decimate = o.modifiers.new("Far silhouette reduction", 'DECIMATE')
            decimate.ratio = .45
            decimate.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=decimate.name)
    return o


def convert(grade, tier):
    kc.clear_scene()
    textures = KIT / "Textures"
    material = kc.pbr_material(f"Building_{grade}", textures / f"T_PSG_{tier}_BaseColor.png", textures / f"T_PSG_{tier}_NormalGL.png",
                               textures / f"T_PSG_{tier}_ORM.png", normal_strength=1.0 if tier == "Twig" else .85, prefix=f"building_{grade}")
    lines = [f"{grade}:"]
    for family, key in KIT_PIECES.items():
        counts = []
        for level, suffix in ((0, ""), (1, "_LOD1"), (2, "_LOD2")):
            objects = kc.import_fbx(KIT / "Exports" / tier / f"SM_PSG_{tier}_{family}{suffix}.fbx")
            hull = 0
            for o in objects:
                if o.type != 'MESH':
                    continue
                if o.name.startswith("UCX_"):
                    if level == 0:
                        kc.collision(o, f"{key}_col{hull:02d}")
                        hull += 1
                    else:
                        bpy.data.objects.remove(o, do_unlink=True)
                    continue
                o.name = key + suffix
                o.data.name = o.name
                kc.drop_lightmap_uvs(o)
                kc.set_material(o, material)
                counts.append(kc.triangles(o))
        lines.append(f"  {key:24} kit {family:20} triangles {counts}")
    for index, key in enumerate(GENERATED):
        seed = 7001 + 1009 * list(TIERS).index(grade) + 137 * index
        counts = []
        for level in range(3):
            o = lod_object(key, tier, seed, level)
            kc.drop_lightmap_uvs(o)
            kc.set_material(o, material)
            counts.append(kc.triangles(o))
        for i, points in enumerate(generated_hulls(key)):
            kc.collision(hull_object(f"{key}_col{i:02d}", points), f"{key}_col{i:02d}")
        lines.append(f"  {key:24} built {'':18} triangles {counts}")
    path = kc.export_glb(OUT / f"building_{grade}.glb")
    lines.append(f"  -> {path.relative_to(kc.REPO)} ({path.stat().st_size // 1024} KB)")
    return lines


def main():
    wanted = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(TIERS)
    lines = []
    for grade in wanted:
        lines += convert(grade, TIERS[grade])
    kc.report(lines, kc.SCRATCH / "building_kit_report.txt")


if __name__ == "__main__":
    main()
