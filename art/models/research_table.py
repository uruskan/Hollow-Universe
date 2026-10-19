"""Original Research Bench: a dismantling and research bench, studied from Rust's public Research Table icon.

A painted steel trestle with a timber top; on it a cutting mat under a magnifier, a device taken apart with its
screws and gear, a screwdriver, a salvage tray, a bench vise and a drawing; below, sample drawers and lidded bins.
It fills its deployable's bounds (deployables.json: half extents 0.75, 0.45, 0.4 m, so 1.5 x 0.9 x 0.8 m) and
faces Blender -Y (the model front) with its footprint centred on the origin at ground level.
No downloaded meshes or pixels; one 2048 px atlas and material, hand-built LODs, deterministic vertex occlusion.

    blender -b --factory-startup --python art/models/research_table.py
"""
from pathlib import Path
import json, math, sys
import bpy
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from hollow import WORLD, TEXTURES, REPO
from hollow.export import collection, export_glb, convex_hull, vertex_ao, set_export_colour
from hollow.godot import write_material
from hollow.textures import write_png, write_texture_import, normal_from_height
from hollow.component_mesh import ComponentMesh
from hollow.diagram_texture import Diagram
from hollow.rounded import rounded_box, weld

OUT = REPO / '.artifacts/itemart/research-table'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('ResearchTable')
HALF = (.75, .40, .45)  # Blender x across, y depth, z half height: the deployable's 0.75, 0.4 (along), 0.45 (up)
TOP = .75  # the work surface
# Top-down atlas regions (x, y, w, h).
regions = {'paint': (.005, .005, .49, .30), 'steel': (.505, .005, .49, .30), 'wood': (.005, .315, .49, .30),
           'mat': (.505, .315, .49, .30), 'print': (.005, .625, .49, .22), 'bin': (.505, .625, .24, .22),
           'rubber': (.755, .625, .24, .22), 'brass': (.005, .855, .24, .14), 'glass': (.255, .855, .24, .14),
           'rust': (.505, .855, .49, .14)}
n = 2048
rng = np.random.default_rng(171171)


def window(name):
    x, y, w, h = regions[name]
    return slice(int(y * n), int((y + h) * n)), slice(int(x * n), int((x + w) * n))


def smooth_field(shape, cells_weights):
    h, w = shape
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    xx /= w; yy /= h
    field = np.zeros(shape, np.float32)
    for cells, weight in cells_weights:
        grid = rng.uniform(-1, 1, (cells + 1, cells + 1)).astype(np.float32)
        gx, gy = xx * cells, yy * cells; ix = gx.astype(int); iy = gy.astype(int)
        fx, fy = gx - ix, gy - iy; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
        field += ((grid[iy, ix] * (1 - fx) + grid[iy, ix + 1] * fx) * (1 - fy)
                  + (grid[iy + 1, ix] * (1 - fx) + grid[iy + 1, ix + 1] * fx) * fy) * weight
    return field


albedo = np.ones((n, n, 4), np.float32)
orm = np.ones_like(albedo); orm[:, :, 2] = 0
height = np.zeros((n, n), np.float32)


def paint_region(name, colour, rough, metal, cells, variation=.08, grain=None):
    ys, xs = window(name)
    shape = (ys.stop - ys.start, xs.stop - xs.start)
    cloud = smooth_field(shape, cells)
    noise = rng.uniform(-1, 1, shape).astype(np.float32)
    albedo[ys, xs, :3] = np.array(colour) * (1 + cloud * variation + noise * .02)[:, :, None]
    orm[ys, xs, 1] = rough + cloud * .05; orm[ys, xs, 2] = metal
    height[ys, xs] = cloud * .02 + noise * .004
    return ys, xs, cloud, noise


def scratches(ys, xs, count, bright, rough_drop, metal_to=None, length=(.01, .06)):
    h, w = ys.stop - ys.start, xs.stop - xs.start
    vv, uu = np.mgrid[0:h, 0:w].astype(np.float32); uu /= w; vv /= h
    for _ in range(count):
        x, y = rng.uniform(.02, .98), rng.uniform(.02, .98)
        width, run, slope = rng.uniform(.0006, .0016), rng.uniform(*length), rng.uniform(-.6, .6)
        y0, y1 = max(0, int((y - run) * h)), min(h, int((y + run) * h) + 1)
        x0, x1 = max(0, int((x - run) * w)), min(w, int((x + run) * w) + 1)
        su, sv = uu[y0:y1, x0:x1], vv[y0:y1, x0:x1]
        stroke = np.exp(-((sv - y - (su - x) * slope) / width) ** 2) * np.clip(1 - np.abs(su - x) / run, 0, 1)
        tile = albedo[ys, xs][y0:y1, x0:x1, :3]
        tile += stroke[:, :, None] * bright
        albedo[ys.start + y0:ys.start + y1, xs.start + x0:xs.start + x1, :3] = tile
        orm[ys.start + y0:ys.start + y1, xs.start + x0:xs.start + x1, 1] -= stroke * rough_drop
        if metal_to is not None:
            m = orm[ys.start + y0:ys.start + y1, xs.start + x0:xs.start + x1, 2]
            orm[ys.start + y0:ys.start + y1, xs.start + x0:xs.start + x1, 2] = np.maximum(m, stroke * metal_to)


# Frame paint: an industrial blue-grey, chipped to bare steel and spotted with rust in places.
ys, xs, cloud, noise = paint_region('paint', (.20, .29, .35), .62, 0, [(5, .6), (17, .3), (61, .12)])
chips = np.clip(smooth_field(cloud.shape, [(90, 1), (230, .5)]) + cloud * .3 - .62, 0, 1) * 4
chips = np.clip(chips, 0, 1)
rusty = np.clip(cloud * 1.3 - .55, 0, 1)
tile = albedo[ys, xs, :3]
tile[:] = tile * (1 - chips[:, :, None]) + np.array([.44, .43, .41])[None, None, :] * chips[:, :, None]
tile[:] = tile * (1 - rusty[:, :, None] * .7) + np.array([.30, .15, .07])[None, None, :] * rusty[:, :, None] * .7
orm[ys, xs, 2] = chips * .9; orm[ys, xs, 1] = .62 - chips * .2 + rusty * .2
height[ys, xs] += chips * -.02
scratches(ys, xs, 60, .10, .1, .8)
# Bare steel: brushed along u, with scratches.
ys, xs, cloud, noise = paint_region('steel', (.47, .47, .46), .42, 1, [(7, .5), (29, .2)])
brush = np.sin(np.mgrid[0:ys.stop - ys.start, 0:xs.stop - xs.start][0].astype(np.float32) * 2.7) * .02
albedo[ys, xs, :3] *= (1 + brush)[:, :, None]
scratches(ys, xs, 80, .08, .12)
# Timber top: long grain along u, a few stains and knife marks.
ys, xs, cloud, noise = paint_region('wood', (.37, .285, .20), .76, 0, [(3, .5), (9, .2)])
h, w = ys.stop - ys.start, xs.stop - xs.start
vv, uu = np.mgrid[0:h, 0:w].astype(np.float32); uu /= w; vv /= h
grain = np.sin((vv * 90 + smooth_field((h, w), [(6, 1.5), (20, .5)]) * 3) * math.tau)
albedo[ys, xs, :3] *= (1 + grain * .10 + cloud * .07)[:, :, None]
boards = (np.abs(((vv * 6) % 1) - .5) > .493)
albedo[ys, xs, :3][boards] *= .55
height[ys, xs] += grain * .01 - boards * .05
stain = np.clip(smooth_field((h, w), [(5, 1)]) - .45, 0, 1) * 1.5
albedo[ys, xs, :3] *= (1 - stain * .3)[:, :, None]
scratches(ys, xs, 40, -.05, -.05, None, (.01, .03))
# Cutting mat: deep green with a fine grid, cut scratches.
ys, xs, cloud, noise = paint_region('mat', (.07, .20, .15), .82, 0, [(5, .4)])
h, w = ys.stop - ys.start, xs.stop - xs.start
vv, uu = np.mgrid[0:h, 0:w].astype(np.float32); uu /= w; vv /= h
line = (np.abs(((uu * 28) % 1) - .5) > .48) | (np.abs(((vv * 20) % 1) - .5) > .48)
major = (np.abs(((uu * 7) % 1) - .5) > .493) | (np.abs(((vv * 5) % 1) - .5) > .492)
albedo[ys, xs, :3][line] = np.array([.20, .42, .31])
albedo[ys, xs, :3][major] = np.array([.55, .70, .58])
scratches(ys, xs, 50, .10, -.05, None, (.01, .05))
# The drawing on the bench: a small blueprint of a lamp-like part, original.
ys, xs, cloud, noise = paint_region('print', (.055, .145, .33), .74, 0, [(4, .5), (13, .2)])
sheet = albedo[ys, xs, :3].copy()
d = Diagram(sheet)
ink = (.80, .88, .93)
d.box(.03, .05, .94, .90, color=ink, width=.003)
d.circle(.30, .45, .16, .22, color=ink, width=.004)
d.circle(.30, .45, .07, .10, color=ink, width=.003)
d.path([(.30, .67), (.30, .86), (.52, .86)], ink, .003)
d.box(.55, .22, .30, .30, color=ink, width=.003)
for k in range(4):
    d.line((.58, .27 + k * .065), (.82, .27 + k * .065), ink, .002, .8)
d.dimension((.10, .15), (.50, .15), color=ink, width=.0018)
d.box(.62, .70, .32, .20, color=ink, width=.0025)
for k in range(3):
    d.line((.65, .75 + k * .05), (.90 - k * .04, .75 + k * .05), ink, .0025, .85)
albedo[ys, xs, :3] = sheet
# Plastic bins, rubber, brass, the lens and rusty scrap.
ys, xs, cloud, noise = paint_region('bin', (.70, .36, .11), .55, 0, [(6, .4), (23, .2)])
scratches(ys, xs, 25, .10, -.1)
paint_region('rubber', (.055, .055, .055), .9, 0, [(8, .5)])
ys, xs, cloud, noise = paint_region('brass', (.60, .46, .22), .38, 1, [(6, .5), (21, .2)])
albedo[ys, xs, :3] *= (1 - np.clip(cloud - .3, 0, 1) * .5)[:, :, None]
paint_region('glass', (.10, .14, .17), .06, .35, [(3, .3)], variation=.04)
ys, xs, cloud, noise = paint_region('rust', (.30, .16, .08), .82, .2, [(5, .6), (18, .3), (47, .16)], variation=.3)
scratches(ys, xs, 30, .12, .15, .7)
paths = {}
for key, pixels in [('albedo', np.clip(albedo, 0, 1)), ('orm', np.clip(orm, 0, 1)), ('normal', normal_from_height(height, 2.0))]:
    paths[key] = write_png(TEXTURES / 'research-table' / f'research-table_{key}.png', pixels)
    write_texture_import(paths[key], normal_map=key == 'normal')
mp = write_material('research-table', 'M_Hollow_ResearchTable', albedo=paths['albedo'], orm=paths['orm'], normal=paths['normal'],
                    normal_scale=.5, vertex_colour=True, vertex_colour_srgb=False)
mat = bpy.data.materials.new('M_Hollow_ResearchTable'); mat.diffuse_color = (.25, .3, .35, 1)


def place(obj, matrix):
    for vertex in obj.data.vertices:
        vertex.co = matrix @ vertex.co
    obj.data.update()
    return obj


def toward(a, b):
    """The matrix that takes +X from the origin onto the segment a-b (its middle at the origin's image)."""
    a, b = Vector(a), Vector(b)
    q = (b - a).normalized().to_track_quat('X', 'Z')
    return Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()


def beam(mesh, name, a, b, size, region, bevel=.003):
    """A square or rectangular tube from a to b: size (across, up) in its own frame."""
    length = (Vector(b) - Vector(a)).length
    obj = mesh.box(name, (0, 0, 0), (length, size[0], size[1]), region, bevel)
    return place(obj, toward(a, b))


def rod(mesh, name, a, b, radius, region, segments=None, end=None):
    length = (Vector(b) - Vector(a)).length
    r2 = end if end is not None else radius
    rings = [(-length / 2, 0), (-length / 2, radius), (length / 2, r2), (length / 2, 0)]
    obj = mesh.lathe_x(name, rings, (0, 0), region, segments=segments or (16, 10, 6)[mesh.lod])
    return place(obj, toward(a, b))


def block(mesh, name, centre, size, region, rotation=0.0, bevel=.002):
    obj = mesh.box(name, (0, 0, 0), size, region, bevel)
    return place(obj, Matrix.Translation(centre) @ Matrix.Rotation(rotation, 4, 'Z'))


def disc(mesh, name, centre, radius, thick, region, matrix=None, segments=None):
    obj = mesh.lathe_x(name, [(-thick / 2, 0), (-thick / 2, radius), (thick / 2, radius), (thick / 2, 0)], (0, 0), region,
                       segments=segments or (24, 14, 8)[mesh.lod])
    return place(obj, Matrix.Translation(centre) @ (matrix or Matrix.Rotation(math.pi / 2, 4, 'Y')))


def ring(mesh, name, centre, radius, tube, region, matrix, steps=None):
    steps = steps or (32, 18, 10)[mesh.lod]
    points = [(radius * math.cos(a), radius * math.sin(a), 0) for a in np.linspace(0, math.tau, steps, endpoint=False)]
    obj = mesh.cable(name, points, tube, region, closed=True, sides=(8, 6, 4)[mesh.lod])
    return place(obj, Matrix.Translation(centre) @ matrix)


def gear(mesh, name, centre, radius, thick, region, teeth=14):
    segments = (teeth * 4, teeth * 2, teeth * 2)[mesh.lod]
    vertices, faces = [], []
    rings = [(radius * .35, 0, False), (radius, 0, True), (radius, thick, True), (radius * .35, thick, False)]
    for rr, z, toothed in rings:
        for j in range(segments):
            a = math.tau * j / segments
            r = rr + (radius * .16 if toothed and (j % 4 in (1, 2) if mesh.lod == 0 else j % 2 == 1) else 0)
            vertices.append((r * math.cos(a), r * math.sin(a), z))
    for k in range(4):
        for j in range(segments):
            jn = (j + 1) % segments
            faces.append((k * segments + j, k * segments + jn, ((k + 1) % 4) * segments + jn, ((k + 1) % 4) * segments + j))
    obj = mesh.mesh(name, vertices, faces, region)
    return place(obj, Matrix.Translation(centre))


objects = []
for lod in range(3):
    mesh = ComponentMesh(col, mat, regions, lod)
    t = .04  # tube section
    lx, ly = .69, .345  # leg centres
    # --- the trestle: four legs on foot plates, side frames with a diagonal brace, rails under the top.
    for sx in (-1, 1):
        for sy in (-1, 1):
            beam(mesh, 'Leg', (sx * lx, sy * ly, .012), (sx * lx, sy * ly, TOP - .044), (t, t), 'paint')
            block(mesh, 'FootPlate', (sx * lx, sy * ly, .006), (.085, .085, .012), 'steel')
        beam(mesh, 'SideStretcher', (sx * lx, -ly, .13), (sx * lx, ly, .13), (t, t * .9), 'paint')
        beam(mesh, 'SideRail', (sx * lx, -ly, TOP - .064), (sx * lx, ly, TOP - .064), (t, t), 'paint')
        beam(mesh, 'SideBrace', (sx * lx, -ly + .02, .15), (sx * lx, ly - .02, TOP - .09), (.028, .022), 'paint')
    for sy in (-1, 1):
        beam(mesh, 'LongRail', (-lx, sy * ly, TOP - .064), (lx, sy * ly, TOP - .064), (t, t), 'paint')
    beam(mesh, 'BackStretcher', (-lx, ly, .13), (lx, ly, .13), (t, t * .9), 'paint')
    # --- the lower shelf with lidded sample bins.
    block(mesh, 'Shelf', (0, 0, .158), (1.34, .66, .012), 'steel', bevel=.002)
    block(mesh, 'ShelfLip', (0, -.33, .175), (1.34, .008, .03), 'paint', bevel=.0015)
    for i, (x, width) in enumerate([(-.47, .30), (-.13, .30), (.21, .26)]):
        vertices, faces = rounded_box((width / 2, .15, .065), .012, (3, 2, 1)[lod], ((3, 2, 1), (2, 1, 1), (1, 1, 1))[lod])
        bin_ = mesh.mesh('SampleBin', vertices, faces, 'bin', True)
        place(bin_, Matrix.Translation((x, .05, .164 + .065)) @ Matrix.Rotation(math.radians((-2, 3, -1)[i]), 4, 'Z'))
        block(mesh, 'BinLid', (x, .05, .164 + .133), (width + .012, .312, .012), 'bin', math.radians((-2, 3, -1)[i]), .003)
        block(mesh, 'BinLabel', (x, -.103, .23), (width * .5, .004, .04), 'print', math.radians((-2, 3, -1)[i]), 0)
    # A coil of wire and a loose plate beside the bins.
    vertices, faces = rounded_box((.10, .10, .006), .004, 2, 1)
    place(mesh.mesh('LoosePlate', vertices, faces, 'rust', True), Matrix.Translation((.53, -.02, .172)) @ Matrix.Rotation(.4, 4, 'Z'))
    ring(mesh, 'WireCoil', (.53, .18, .185), .07, .012, 'brass', Matrix.Identity(4))
    ring(mesh, 'WireCoil', (.53, .18, .205), .066, .011, 'brass', Matrix.Rotation(.2, 4, 'Z'))
    # --- the drawers: sample storage hung under the top on the right.
    dx = .42
    block(mesh, 'DrawerCase', (dx, .02, .581), (.40, .62, .25), 'paint', bevel=.004)
    for k, z in enumerate((.64, .51)):
        block(mesh, 'DrawerFront', (dx, -.292, z), (.37, .012, .105), 'paint', bevel=.003)
        rod(mesh, 'DrawerHandle', (dx - .07, -.31, z), (dx + .07, -.31, z), .008, 'steel')
        for hx in (dx - .07, dx + .07):
            block(mesh, 'HandlePost', (hx, -.302, z), (.012, .02, .012), 'steel', 0, .001)
        block(mesh, 'DrawerTag', (dx, -.299, z - .03), (.07, .003, .022), 'print', 0, 0)
    # --- the top: timber boards in an angle-steel frame.
    block(mesh, 'Worktop', (0, 0, TOP - .022), (1.44, .74, .044), 'wood', bevel=.004)
    for sy in (-1, 1):
        block(mesh, 'EdgeBand', (0, sy * .373, TOP - .024), (1.46, .008, .052), 'steel', bevel=.0015)
    for sx in (-1, 1):
        block(mesh, 'EdgeBand', (sx * .727, 0, TOP - .024), (.008, .754, .052), 'steel', bevel=.0015)
    # --- on the top: the cutting mat, the device being taken apart and its parts.
    block(mesh, 'CuttingMat', (-.20, -.03, TOP + .002), (.66, .48, .004), 'mat', 0, 0)
    case_at = Vector((-.22, -.08, TOP + .004))
    vertices, faces = rounded_box((.085, .055, .022), .008, (3, 2, 1)[lod], ((3, 2, 1), (2, 1, 1), (1, 1, 1))[lod])
    place(mesh.mesh('DeviceCase', vertices, faces, 'paint', True), Matrix.Translation(case_at + Vector((0, 0, .022))) @ Matrix.Rotation(.25, 4, 'Z'))
    block(mesh, 'DeviceBoard', case_at + Vector((0, 0, .046)), (.13, .08, .004), 'mat', .25, 0)
    for k in range(3):
        block(mesh, 'BoardChip', case_at + Vector((-.035 + k * .033, .006 * k, .051)), (.022, .016, .006), 'rubber', .25, .0008)
    vertices, faces = rounded_box((.085, .055, .007), .006, (3, 2, 1)[lod], ((3, 2, 1), (2, 1, 1), (1, 1, 1))[lod])
    place(mesh.mesh('DeviceLid', vertices, faces, 'paint', True), Matrix.Translation((-.03, -.13, TOP + .011)) @ Matrix.Rotation(-.5, 4, 'Z'))
    gear(mesh, 'LooseGear', (-.36, -.17, TOP + .004), .03, .008, 'brass')
    for k, (sx, sy) in enumerate([(-.10, -.20), (-.085, -.215), (-.07, -.195)]):
        rod(mesh, 'Screw', (sx, sy, TOP + .008), (sx + .018 * math.cos(k * 1.7), sy + .018 * math.sin(k * 1.7), TOP + .008), .0035, 'steel', (8, 6, 4)[lod])
    # Screwdriver: a rubber handle and a steel shaft.
    rod(mesh, 'DriverHandle', (.02, -.06, TOP + .016), (.12, -.02, TOP + .016), .013, 'rubber', end=.011)
    rod(mesh, 'DriverShaft', (.12, -.02, TOP + .016), (.24, .028, TOP + .012), .0035, 'steel', (8, 6, 4)[lod])
    # The salvage tray at the left end, scrap in it.
    tray = Vector((-.58, -.10, TOP))
    block(mesh, 'TrayBase', tray + Vector((0, 0, .004)), (.20, .24, .006), 'steel', 0, .001)
    for sx in (-1, 1):
        block(mesh, 'TrayWall', tray + Vector((sx * .097, 0, .022)), (.006, .24, .04), 'steel', 0, .001)
    for sy in (-1, 1):
        block(mesh, 'TrayWall', tray + Vector((0, sy * .117, .022)), (.20, .006, .04), 'steel', 0, .001)
    for k, (ox, oy, rot) in enumerate([(-.04, -.05, .3), (.03, .04, -.6), (-.02, .06, 1.2), (.05, -.06, .9)]):
        block(mesh, 'Scrap', tray + Vector((ox, oy, .016 + k * .004)), (.07, .05, .01), 'rust', rot, .002)
    gear(mesh, 'ScrapGear', tray + Vector((.0, -.01, .03)), .035, .008, 'rust')
    rod(mesh, 'ScrapPipe', tray + Vector((-.07, .09, .03)), tray + Vector((.06, .02, .03)), .012, 'rust', (10, 8, 5)[lod])
    # The drawing on an inclined drafting board at the back right, and the vise at the front right corner.
    slope = math.radians(18)
    board = Matrix.Translation((.27, .19, TOP + .066)) @ Matrix.Rotation(math.radians(-4), 4, 'Z') @ Matrix.Rotation(slope, 4, 'X')
    place(mesh.box('DraftingBoard', (0, 0, 0), (.44, .30, .014), 'wood', .003), board)
    place(mesh.box('Drawing', (0, -.005, .0082), (.36, .25, .002), 'print', 0), board)
    place(mesh.box('BoardClip', (0, .135, .013), (.30, .018, .012), 'steel', .002), board)
    place(mesh.box('BoardLedge', (0, -.152, .012), (.42, .012, .022), 'steel', .002), board)
    for sx in (-.19, .19):
        place(mesh.box('BoardStand', (sx, .10, -.045), (.018, .05, .08), 'paint', .002), board)
        place(mesh.box('BoardStand', (sx, -.10, -.012), (.018, .04, .02), 'paint', .002), board)
    vise = Vector((.56, -.25, TOP))
    block(mesh, 'ViseBase', vise + Vector((0, .02, .012)), (.15, .12, .024), 'paint', 0, .003)
    block(mesh, 'ViseBody', vise + Vector((0, .03, .06)), (.10, .11, .072), 'paint', 0, .004)
    block(mesh, 'ViseJawFixed', vise + Vector((0, -.03, .104)), (.12, .022, .028), 'steel', 0, .002)
    block(mesh, 'ViseJawMoving', vise + Vector((0, -.09, .104)), (.12, .022, .028), 'steel', 0, .002)
    block(mesh, 'ViseSlide', vise + Vector((0, -.06, .075)), (.05, .08, .034), 'paint', 0, .003)
    rod(mesh, 'ViseScrew', vise + Vector((0, -.10, .075)), vise + Vector((0, -.135, .075)), .008, 'steel', (10, 8, 5)[lod])
    rod(mesh, 'ViseHandle', vise + Vector((-.07, -.135, .075)), vise + Vector((.07, -.135, .075)), .005, 'steel', (8, 6, 4)[lod])
    for sx in (-1, 1):
        disc(mesh, 'HandleKnob', vise + Vector((sx * .072, -.135, .075)), .009, .012, 'rubber', Matrix.Identity(4), (12, 8, 6)[lod])
    # The magnifier: a clamp at the back left, a post and arm, the lens over the device.
    base = Vector((-.56, .30, TOP))
    block(mesh, 'LampClamp', base + Vector((0, 0, .012)), (.06, .06, .024), 'steel', 0, .002)
    rod(mesh, 'LampPost', base + Vector((0, 0, .02)), base + Vector((0, 0, .118)), .009, 'steel')
    lens = case_at + Vector((.0, .02, .096))
    elbow = base + Vector((0, 0, .118))
    rod(mesh, 'LampArm', elbow, lens + Vector((-.07, .045, .016)), .007, 'steel')
    disc(mesh, 'LampJoint', elbow, .014, .022, 'rubber', Matrix.Rotation(math.pi / 2, 4, 'Y'), (12, 8, 6)[lod])
    tilt = Matrix.Rotation(math.radians(24), 4, 'X') @ Matrix.Rotation(math.radians(8), 4, 'Y')
    ring(mesh, 'LensRing', lens, .074, .011, 'steel', tilt)
    ring(mesh, 'LensBezel', lens, .066, .006, 'rubber', tilt)
    disc(mesh, 'Lens', lens, .068, .006, 'glass', tilt @ Matrix.Rotation(math.pi / 2, 4, 'Y'), (28, 16, 10)[lod])
    rod(mesh, 'LensHandle', lens + Vector((.07, -.02, -.01)), lens + Vector((.13, -.05, -.03)), .008, 'rubber', end=.007)
    obj = mesh.finish(f'Body_lod{lod}')
    ao = vertex_ao([obj], samples=8, distance=.22, floor=True)[obj]
    colors = np.ones((len(ao), 4), np.float32); colors[:, :3] = (.55 + .45 * ao)[:, None]
    set_export_colour(obj, colors)
    objects.append(obj)
objects.append(convex_hull([objects[0]], 'Body_hull0-convcolonly', col, max_faces=48))
use = bpy.data.objects.new('socket_use', None)
col.objects.link(use)
use.location = (0, -HALF[1] - .02, TOP + .05)
verts = [v.co for v in objects[0].data.vertices]
lo = [min(v[i] for v in verts) for i in range(3)]
hi = [max(v[i] for v in verts) for i in range(3)]
bounds = {'min': lo, 'max': hi, 'deployable_half_extents_xyz_m': [HALF[0], HALF[1], HALF[2]],
          'inside_bounds': all(-HALF[i] - 1e-6 <= lo[i] and hi[i] <= HALF[i] + 1e-6 for i in (0, 1)) and lo[2] >= -1e-6 and hi[2] <= 2 * HALF[2]}
report = {'asset': 'research-table', 'dimensions_blender_xyz_m': [h - l for h, l in zip(hi, lo)], 'bounds': bounds,
          'texture_size': n, 'material_count': 1,
          'lod_triangles': {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects[:3]},
          'hull_triangles': sum(len(p.vertices) - 2 for p in objects[-1].data.polygons), 'vertex_ao_samples': 8,
          'sockets': {'socket_use': list(use.location)}}
export_glb(WORLD / 'research-table.glb', objects + [use], {'M_Hollow_ResearchTable': mp})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'research-table-source.blend'))
(OUT / 'geometry.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report), flush=True)
if not bounds['inside_bounds']:
    raise SystemExit(f'research-table outside its deployable bounds: {bounds}')
