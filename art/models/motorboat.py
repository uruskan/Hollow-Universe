"""Original Motorboat: a small open boat welded from salvaged sheet, an outboard on its transom (docs/MOTORBOAT.md §7).

A hard-chined skiff 4.4 m long and 1.6 m in the beam: a faded teal hull of welded panels with a rust-red patch, a
dark bottom, a plank deck raised clear of the waterline (the sea is one plane, so nothing under it may show), two
thwarts, a car's steering wheel on a welded console cabled aft to a weathered outboard, a red fuel can, a lashed crate
in the bow, a lamp on the stem, rope and cleats. Ship frame (ships.json): +Z the bow, +Y up, +X port, the pivot on the
ground under the keel at the middle of the hull's length; Blender has the bow toward -Y (S() converts). No downloaded
meshes or pixels: one 2048 px atlas and material, hand-built LODs, deterministic vertex occlusion.

    blender -b --factory-startup --python art/models/motorboat.py [-- --preview <png prefix>]
"""
from pathlib import Path
import json, math, sys
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from hollow import WORLD, TEXTURES, REPO, script_args
from hollow.export import collection, export_glb, convex_hull, vertex_ao, set_export_colour
from hollow.godot import write_material
from hollow.textures import write_png, write_texture_import, normal_from_height
from hollow.component_mesh import ComponentMesh
from hollow.rounded import rounded_box

OUT = REPO / '.artifacts/itemart/motorboat'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('Motorboat')

# ---------------------------------------------------------------- the boat's measures (ship frame, metres)
TRANSOM, BOW = -2.05, 2.35      # the transom's plane and the stem's head
DECK = 0.26                     # the deck's top: above the waterline (about 0.15), so the sea never shows inside
TH = 0.025                      # the hull's sheet, as drawn
SEAT = 0.60                     # a thwart's top (the seats' cushions are 2 cm over it: ships.json seats at 0.62)
HELM_Z, BENCH_Z = -1.45, 0.20   # the thwarts (ships.json seats)
WHEEL = (0.0, 0.94, -1.03)      # ShipView's helm grips: 0.32 over the seat, 0.42 ahead, 0.2 either side
PROP = (0.0, -0.02, -2.34)      # ships.json drive.at
FOREDECK = 1.86                 # the foredeck's aft edge
LAMP_Z = 2.02                   # the bow lamp's station (ships.json lights: at its housing)


def S(x, y, z):
    """Ship frame to Blender: x port, y up, z bow -> Blender x, -y (the bow toward -Y), z up."""
    return (x, -z, y)


def SV(p):
    return Vector(S(*p))


def section(z):
    """A station's outer half-section: gunwale, the topside's knuckle, chine and keel, as (x, y) pairs from the gunwale."""
    bow = min(1.0, max(0.0, (z - 0.25) / (BOW - 0.25)))
    stern = max(0.0, (-1.2 - z) / (-1.2 - TRANSOM))
    # The plan closes on the centreline at the stem (both sides meet there: no opening at the bow), full forward.
    xg = 0.82 * max(0.0, math.cos(math.pi / 2 * bow)) ** 0.7 * (1 - 0.06 * stern)
    run = min(1.0, max(0.0, (z - 0.25) / (1.95 - 0.25)))
    xc = 0.55 * max(0.0, math.cos(math.pi / 2 * run)) ** 0.7 * (1 - 0.04 * stern)
    yk = 0.0 if z <= 0.6 else 0.50 * ((z - 0.6) / (BOW - 0.6)) ** 1.8
    yc = yk + 0.08 * (xc / 0.55)
    yg = 0.80 + 0.22 * bow ** 1.8
    xf, yf = xc + (xg - xc) * 0.88, yc + (yg - yc) * 0.38
    return [(xg, yg), (xf, yf), (xc, yc), (0.0, yk)]


def low_inside(points, level):
    """Where the inside of a half-section reaches down to: the outer line at the deck's height, or the keel above it."""
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        if y0 >= level >= y1:
            t = (level - y1) / max(y0 - y1, 1e-6)
            return (x1 + (x0 - x1) * t, level)
    return (0.0, points[-1][1])


def stations(lod):
    aft = np.linspace(TRANSOM, 0.25, (14, 8, 4)[lod])
    fore = 0.25 + (BOW - 0.25) * np.sin(np.linspace(0, math.pi / 2, (14, 8, 5)[lod]))[1:]   # the last at the stem
    return list(aft) + list(fore)


# ---------------------------------------------------------------- the atlas
regions = {'hull': (.005, .005, .49, .43), 'bottom': (.505, .005, .49, .21), 'inner': (.505, .225, .49, .21),
           'deck': (.005, .445, .49, .25), 'wood': (.505, .445, .24, .25), 'motor': (.755, .445, .24, .25),
           'steel': (.005, .705, .24, .14), 'rubber': (.255, .705, .115, .14), 'canvas': (.38, .705, .115, .14),
           'rust': (.505, .705, .24, .14), 'can': (.755, .705, .115, .14), 'glass': (.88, .705, .115, .14),
           'rope': (.005, .855, .24, .14), 'patch': (.255, .855, .24, .14), 'crate': (.505, .855, .24, .14),
           'paint': (.755, .855, .24, .14)}
n = 2048
rng = np.random.default_rng(239239)


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


def paint_region(name, colour, rough, metal, cells, variation=.08):
    ys, xs = window(name)
    shape = (ys.stop - ys.start, xs.stop - xs.start)
    cloud = smooth_field(shape, cells)
    noise = rng.uniform(-1, 1, shape).astype(np.float32)
    albedo[ys, xs, :3] = np.array(colour) * (1 + cloud * variation + noise * .02)[:, :, None]
    orm[ys, xs, 1] = rough + cloud * .05; orm[ys, xs, 2] = metal
    height[ys, xs] = cloud * .02 + noise * .004
    return ys, xs, cloud, noise


def grid_uv(ys, xs):
    h, w = ys.stop - ys.start, xs.stop - xs.start
    vv, uu = np.mgrid[0:h, 0:w].astype(np.float32)
    return uu / w, vv / h


def blend(ys, xs, mask, colour, rough=None, metal=None, amount=1.0):
    m = np.clip(mask, 0, 1)[:, :, None] * amount
    albedo[ys, xs, :3] = albedo[ys, xs, :3] * (1 - m) + np.array(colour)[None, None, :] * m
    if rough is not None:
        orm[ys, xs, 1] = orm[ys, xs, 1] * (1 - m[:, :, 0]) + rough * m[:, :, 0]
    if metal is not None:
        orm[ys, xs, 2] = orm[ys, xs, 2] * (1 - m[:, :, 0]) + metal * m[:, :, 0]


def welded(ys, xs, seams_u, seams_v, rivets=True):
    """Welded seams along u at the given fractions and across v, a bead of darker metal with rivets beside it."""
    uu, vv = grid_uv(ys, xs)
    bead = np.zeros_like(uu)
    for s in seams_u:
        bead = np.maximum(bead, np.exp(-((uu - s) / .0022) ** 2))
    for s in seams_v:
        bead = np.maximum(bead, np.exp(-((vv - s) / .004) ** 2))
    blend(ys, xs, bead, (.20, .19, .18), .55, .6, .85)
    height[ys, xs] += bead * .03
    if rivets:
        dots = np.zeros_like(uu)
        for s in seams_u:
            phase = (vv * 46) % 1
            dots = np.maximum(dots, np.exp(-(((uu - s - .006) / .0016) ** 2 + ((phase - .5) / .09) ** 2)))
        blend(ys, xs, dots, (.30, .29, .27), .45, .8, .9)
        height[ys, xs] += dots * .04


def streaks(ys, xs, count, colour, strength=.5):
    """Rust running down (toward v=1) from points on the hull."""
    uu, vv = grid_uv(ys, xs)
    mask = np.zeros_like(uu)
    for _ in range(count):
        u0, v0 = rng.uniform(.02, .98), rng.uniform(0, .5)
        width, run = rng.uniform(.002, .007), rng.uniform(.15, .6)
        s = np.exp(-((uu - u0 - (vv - v0) * rng.uniform(-.02, .02)) / width) ** 2) * np.clip(1 - (vv - v0) / run, 0, 1) * (vv > v0)
        mask = np.maximum(mask, s * rng.uniform(.4, 1))
    blend(ys, xs, mask, colour, .8, .1, strength)


# The hull: a faded teal over steel, chipped and streaked, welded panels along its length.
ys, xs, cloud, noise = paint_region('hull', (.17, .36, .37), .58, 0, [(4, .6), (13, .3), (47, .12)], .12)
chips = np.clip(smooth_field(cloud.shape, [(70, 1), (180, .5)]) + cloud * .35 - .6, 0, 1) * 3
blend(ys, xs, chips, (.40, .39, .37), .45, .85)
height[ys, xs] -= np.clip(chips, 0, 1) * .02
faded = np.clip(smooth_field(cloud.shape, [(3, 1)]) * .8 + .2, 0, 1)
albedo[ys, xs, :3] *= (0.9 + faded * .15)[:, :, None]
welded(ys, xs, [.12, .27, .41, .55, .69, .83], [.36, .7])
streaks(ys, xs, 40, (.33, .16, .07), .55)
uu, vv = grid_uv(ys, xs)
blend(ys, xs, ((uu > .30) & (uu < .38) & (vv > .38) & (vv < .66)).astype(np.float32), (.38, .14, .08), .7, .1, .9)  # a primer-red patch
welded(ys, xs, [.30, .38], [], rivets=True)
# The bottom: dark antifouling, worn to steel along the keel.
ys, xs, cloud, noise = paint_region('bottom', (.13, .07, .05), .78, 0, [(5, .5), (19, .3)], .2)
streaks(ys, xs, 12, (.25, .22, .18), .3)
welded(ys, xs, [.25, .5, .75], [.5], rivets=False)
# The inside: grey deck paint, scuffed where feet and cargo go.
ys, xs, cloud, noise = paint_region('inner', (.33, .34, .33), .7, 0, [(5, .5), (17, .3)], .12)
blend(ys, xs, np.clip(smooth_field(cloud.shape, [(40, 1)]) - .35, 0, 1) * 2, (.42, .40, .37), .55, .7, .7)
welded(ys, xs, [.2, .45, .7], [], rivets=False)
streaks(ys, xs, 15, (.30, .17, .08), .35)
# The deck: planks running along the boat (along v), screwed down.
ys, xs, cloud, noise = paint_region('deck', (.40, .31, .22), .8, 0, [(3, .5), (9, .2)])
uu, vv = grid_uv(ys, xs)
grain = np.sin((uu * 120 + smooth_field(uu.shape, [(6, 1.5), (20, .5)]) * 3) * math.tau)
albedo[ys, xs, :3] *= (1 + grain * .09 + cloud * .08)[:, :, None]
boards = (np.abs(((uu * 8) % 1) - .5) > .485)
albedo[ys, xs, :3][boards] *= .45
height[ys, xs] += grain * .01 - boards * .06
weather = np.clip(smooth_field(uu.shape, [(4, 1)]) + .1, 0, 1)
blend(ys, xs, weather * .5, (.47, .44, .40), .9)
# Thwarts and the crate: plain timber, grain along u.
for name, colour in (('wood', (.43, .33, .22)), ('crate', (.36, .30, .21))):
    ys, xs, cloud, noise = paint_region(name, colour, .78, 0, [(3, .5), (9, .2)])
    uu, vv = grid_uv(ys, xs)
    grain = np.sin((vv * 70 + smooth_field(uu.shape, [(6, 1.5)]) * 3) * math.tau)
    albedo[ys, xs, :3] *= (1 + grain * .1)[:, :, None]
    if name == 'crate':
        slats = (np.abs(((vv * 5) % 1) - .5) > .47)
        albedo[ys, xs, :3][slats] *= .5
        height[ys, xs] -= slats * .05
# The outboard's cowl: a faded orange, scuffed to the grey under it.
ys, xs, cloud, noise = paint_region('motor', (.62, .29, .07), .5, 0, [(4, .5), (15, .3)], .15)
blend(ys, xs, np.clip(smooth_field(cloud.shape, [(55, 1), (150, .4)]) + cloud * .3 - .55, 0, 1) * 3, (.30, .30, .29), .5, .2)
uu, vv = grid_uv(ys, xs)
blend(ys, xs, ((vv > .62) & (vv < .66)).astype(np.float32), (.08, .08, .08), .6, 0)  # a black band round the cowl
ys, xs, cloud, noise = paint_region('steel', (.46, .46, .45), .4, 1, [(7, .5), (29, .2)])
paint_region('rubber', (.05, .05, .05), .85, 0, [(8, .5)])
ys, xs, cloud, noise = paint_region('canvas', (.30, .32, .22), .9, 0, [(6, .5), (25, .2)], .15)
uu, vv = grid_uv(ys, xs)
albedo[ys, xs, :3] *= (1 + np.sin(uu * 400) * .03 * np.sin(vv * 400))[:, :, None]
ys, xs, cloud, noise = paint_region('rust', (.32, .16, .08), .82, .2, [(5, .6), (18, .3), (47, .16)], .3)
ys, xs, cloud, noise = paint_region('can', (.55, .08, .05), .45, 0, [(4, .4), (17, .2)], .12)
blend(ys, xs, np.clip(smooth_field(cloud.shape, [(60, 1)]) + cloud * .3 - .6, 0, 1) * 3, (.38, .37, .35), .45, .8)
paint_region('glass', (.75, .72, .62), .08, 0, [(3, .2)], .03)
ys, xs, cloud, noise = paint_region('rope', (.55, .47, .33), .9, 0, [(6, .4)], .1)
uu, vv = grid_uv(ys, xs)
twist = np.sin((uu * 60 + vv * 60) * math.tau)
albedo[ys, xs, :3] *= (1 + twist * .15)[:, :, None]
height[ys, xs] += twist * .03
ys, xs, cloud, noise = paint_region('patch', (.38, .14, .08), .7, .1, [(5, .5), (17, .3)], .15)
welded(ys, xs, [.05, .95], [.05, .95])
ys, xs, cloud, noise = paint_region('paint', (.17, .36, .37), .55, 0, [(4, .6), (13, .3)], .12)
blend(ys, xs, np.clip(smooth_field(cloud.shape, [(50, 1)]) + cloud * .3 - .6, 0, 1) * 3, (.40, .39, .37), .45, .85)

paths = {}
for key, pixels in [('albedo', np.clip(albedo, 0, 1)), ('orm', np.clip(orm, 0, 1)), ('normal', normal_from_height(height, 2.0))]:
    paths[key] = write_png(TEXTURES / 'motorboat' / f'motorboat_{key}.png', pixels)
    write_texture_import(paths[key], normal_map=key == 'normal')
mp = write_material('motorboat', 'M_Hollow_Motorboat', albedo=paths['albedo'], orm=paths['orm'], normal=paths['normal'],
                    normal_scale=.5, vertex_colour=True, vertex_colour_srgb=False)
mat = bpy.data.materials.new('M_Hollow_Motorboat'); mat.diffuse_color = (.2, .35, .36, 1)


# ---------------------------------------------------------------- helpers (as the research bench's)
def place(obj, matrix):
    for vertex in obj.data.vertices:
        vertex.co = matrix @ vertex.co
    obj.data.update()
    return obj


def toward(a, b):
    a, b = Vector(a), Vector(b)
    q = (b - a).normalized().to_track_quat('X', 'Z')
    return Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()


def beam(mesh, name, a, b, size, region, bevel=.003):
    """A square tube from ship point a to b: size (across, up)."""
    a, b = SV(a), SV(b)
    obj = mesh.box(name, (0, 0, 0), ((b - a).length, size[0], size[1]), region, bevel)
    return place(obj, toward(a, b))


def rod(mesh, name, a, b, radius, region, segments=None, end=None):
    a, b = SV(a), SV(b)
    length = (b - a).length
    r2 = end if end is not None else radius
    rings = [(-length / 2, 0), (-length / 2, radius), (length / 2, r2), (length / 2, 0)]
    obj = mesh.lathe_x(name, rings, (0, 0), region, segments=segments or (16, 10, 6)[mesh.lod])
    return place(obj, toward(a, b))


def block(mesh, name, centre, size, region, yaw=0.0, bevel=.003, matrix=None):
    """A box at a ship point, size (across x, up y, along z) in the ship frame."""
    obj = mesh.box(name, (0, 0, 0), (size[0], size[2], size[1]), region, bevel)
    m = Matrix.Translation(SV(centre)) @ Matrix.Rotation(-yaw, 4, 'Z')
    return place(obj, m @ (matrix or Matrix.Identity(4)))


def soft(mesh, name, centre, half, radius, region, matrix=None):
    """A rounded box (half extents in the ship frame) at a ship point."""
    lod = mesh.lod
    vertices, faces = rounded_box((half[0], half[2], half[1]), radius, (3, 2, 1)[lod], ((3, 2, 1), (2, 1, 1), (1, 1, 1))[lod])
    obj = mesh.mesh(name, vertices, faces, region, True)
    return place(obj, Matrix.Translation(SV(centre)) @ (matrix or Matrix.Identity(4)))


def cable(mesh, name, points, radius, region, closed=False):
    return mesh.cable(name, [S(*p) for p in points], radius, region, closed=closed, sides=(8, 6, 4)[mesh.lod])


def strip(mesh, name, left, right, region, smooth=True):
    """Quads between two polylines of ship points (equal counts), facing by their order."""
    vertices = [S(*p) for p in left] + [S(*p) for p in right]
    k = len(left)
    faces = [(i, i + 1, k + i + 1, k + i) for i in range(k - 1)]
    return mesh.mesh(name, vertices, faces, region, smooth)


def grid(mesh, name, rows, region, smooth=True):
    """Quads over a grid of ship points (rows of equal length)."""
    vertices = [S(*p) for row in rows for p in row]
    k = len(rows[0])
    faces = [(r * k + i, r * k + i + 1, (r + 1) * k + i + 1, (r + 1) * k + i) for r in range(len(rows) - 1) for i in range(k - 1)]
    return mesh.mesh(name, vertices, faces, region, smooth)


def ngon(mesh, name, points, region):
    return mesh.mesh(name, [S(*p) for p in points], [tuple(range(len(points)))], region, False)


def orient(obj, want):
    """Turns each face of a one-sided panel toward where it is seen from: `want(centre)` (Blender) is the way it must face.
    (ComponentMesh recalculates normals as for a closed solid, which may turn an open panel the wrong way; the game
    culls back faces.)"""
    bm = bmesh.new(); bm.from_mesh(obj.data)
    for face in bm.faces:
        if face.normal.dot(want(face.calc_center_median())) < 0:
            face.normal_flip()
    bm.to_mesh(obj.data); bm.free()
    obj.data.update()
    return obj


UP = lambda c: Vector((0, 0, 1))
DOWN = lambda c: Vector((0, 0, -1))
AFT = lambda c: Vector((0, 1, 0))       # the ship's -Z is Blender's +Y
AHEAD = lambda c: Vector((0, -1, 0))
def _middle(c):
    """The hull's middle at a Blender point's station (between the keel and the gunwale), in Blender."""
    h = section(min(max(-c.y, TRANSOM), BOW - .02))
    return Vector((0, c.y, (h[-1][1] + h[0][1]) / 2))


OUTWARD = lambda c: c - _middle(c)   # away from the hull's middle at that station
INWARD = lambda c: _middle(c) - c


# ---------------------------------------------------------------- the boat, one level of detail at a time
def hull(mesh):
    lod = mesh.lod
    zs = stations(lod)
    halves = [section(z) for z in zs]
    # Outer skin: each side's topside (gunwale, knuckle, chine) and bottom (chine, keel) apart, so chine and keel stay sharp.
    for side in (1, -1):
        top_rows = [[(side * x, y, z) for x, y in h[:3]] for z, h in zip(zs, halves)]
        bottom_rows = [[(side * x, y, z) for x, y in h[2:]] for z, h in zip(zs, halves)]
        orient(grid(mesh, 'Topside', top_rows, 'hull'), OUTWARD)
        orient(grid(mesh, 'Bottom', bottom_rows, 'bottom'), OUTWARD)
    # Inner skin from 5 cm under the deck (the deck's edge overlaps it: no crack to see the sea through) up to the
    # gunwale's cap; where the bottom rises above that, down to the keel.
    inner_rows, caps = [], []
    for z, h in zip(zs, halves):
        lo = low_inside(h, max(DECK - .05, h[-1][1]))
        row = [(max(h[0][0] - TH, 0.0), h[0][1]), (max(h[1][0] - TH, 0.0), h[1][1]), (max(lo[0] - TH, 0.0), lo[1])]
        inner_rows.append(row)
        caps.append(((h[0][0], h[0][1]), (max(h[0][0] - TH, 0.0), h[0][1])))
    for side in (1, -1):
        rows = [[(side * x, y, z) for x, y in row] for z, row in zip(zs, inner_rows)]
        orient(grid(mesh, 'Inside', rows, 'inner'), INWARD)
        orient(strip(mesh, 'GunwaleCap', [(side * c[0][0], c[0][1], z) for z, c in zip(zs, caps)],
                     [(side * c[1][0], c[1][1], z) for z, c in zip(zs, caps)], 'paint', False), UP)
    # The transom: its outer plate, its inner face above the deck and its top.
    h0 = halves[0]
    outer = [(x, y, TRANSOM) for x, y in h0] + [(-x, y, TRANSOM) for x, y in reversed(h0[:-1])]
    orient(ngon(mesh, 'Transom', outer, 'hull'), AFT)
    r0 = inner_rows[0]
    inner = [(x, y, TRANSOM + TH) for x, y in reversed(r0)] + [(-x, y, TRANSOM + TH) for x, y in r0]
    orient(ngon(mesh, 'TransomInside', inner, 'inner'), AHEAD)
    orient(strip(mesh, 'TransomCap', [(h0[0][0], h0[0][1], TRANSOM), (-h0[0][0], h0[0][1], TRANSOM)],
                 [(r0[0][0], r0[0][1], TRANSOM + TH), (-r0[0][0], r0[0][1], TRANSOM + TH)], 'paint', False), UP)
    # The deck: planks from the transom to where the bottom rises through it, a centimetre into the walls at each side,
    # closed to the keel at the first station where the bottom is above it (no gap between the deck and the bow).
    deck = []
    for z, h in zip(zs, halves):
        x = low_inside(h, DECK)[0] - TH
        if h[-1][1] < DECK and x > .02:
            deck.append([(x + .01, DECK, z), (-x - .01, DECK, z)])
        elif deck:
            deck.append([(0.0, h[-1][1], z), (0.0, h[-1][1], z)])
            break
    orient(grid(mesh, 'Deck', deck, 'deck', False), UP)
    # A rubbing strake along each gunwale, a stem post and a keel bar.
    for side in (1, -1):
        cable(mesh, 'Strake', [(side * (h[0][0] + .012), h[0][1] - .035, z) for z, h in zip(zs, halves)], .022, 'rubber')
    # The stem bar along the keel's rise and up the stem, where the sides meet.
    stem = [(0, section(z)[-1][1] - .018, z) for z in np.linspace(1.2, BOW, (8, 5, 3)[lod])]
    stem += [(0, (section(BOW)[-1][1] + section(BOW)[0][1]) / 2, BOW + .018), (0, section(BOW)[0][1] + .02, BOW + .018)]
    cable(mesh, 'Stem', stem, .022, 'steel')
    cable(mesh, 'KeelBar', [(0, -.01, z) for z in np.linspace(TRANSOM + .05, 1.25, (6, 4, 2)[lod])], .018, 'steel')


def fittings(mesh):
    lod = mesh.lod
    # Thwarts: timber benches on welded knees, a canvas cushion at each seat.
    for z, name in ((HELM_Z, 'Helm'), (BENCH_Z, 'Bench')):
        half = section(z)[0][0] - TH - .03
        block(mesh, name + 'Thwart', (0, SEAT - .02, z), (2 * half, .04, .30), 'wood', bevel=.004)
        for sx in (-1, 1):
            block(mesh, name + 'Knee', (sx * (half - .04), SEAT - .12, z), (.05, .16, .22), 'inner', bevel=.003)
        if lod < 2:
            block(mesh, name + 'Leg', (0, (DECK + SEAT - .04) / 2, z), (.08, SEAT - .04 - DECK, .06), 'inner', bevel=.003)
        soft(mesh, name + 'Cushion', (0, SEAT + .012, z), (.23, .022, .14), .018, 'canvas')
    # The console: a welded box, a dash plate, the wheel on its post, a throttle lever, the cables aft.
    cx, cy, cz = WHEEL
    block(mesh, 'Console', (0, (DECK + .80) / 2, cz + .10), (.34, .80 - DECK, .20), 'paint', bevel=.006)
    block(mesh, 'Dash', (0, .82, cz + .06), (.38, .05, .30), 'steel', bevel=.004,
          matrix=Matrix.Rotation(math.radians(20), 4, 'X'))
    rod(mesh, 'SteeringColumn', (0, .80, cz + .10), (cx, cy, cz), .03, 'steel')
    tilt = Matrix.Rotation(math.radians(-35), 4, 'X')  # the wheel's face toward the driver, leaning back
    at = Matrix.Translation(SV(WHEEL)) @ tilt
    steps = (36, 20, 12)[lod]
    rim = [(.2 * math.cos(a), .2 * math.sin(a), 0) for a in np.linspace(0, math.tau, steps, endpoint=False)]
    place(mesh.cable('WheelRim', rim, .016, 'rubber', closed=True, sides=(10, 6, 4)[lod]), at)
    for a in (math.pi / 2, math.pi * 7 / 6, math.pi * 11 / 6):
        spoke = mesh.box('WheelSpoke', (0, 0, 0), (.19, .014, .03), 'steel', .002)
        place(spoke, at @ Matrix.Rotation(a, 4, 'Z') @ Matrix.Translation((.095, 0, 0)))
    hub = mesh.lathe_x('WheelHub', [(-.03, 0), (-.03, .045), (.02, .04), (.02, 0)], (0, 0), 'rubber', segments=(16, 10, 6)[lod])
    place(hub, at @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
    if lod < 2:
        rod(mesh, 'ThrottleLever', (.19, .80, cz + .05), (.24, .95, cz - .02), .012, 'steel')
        soft(mesh, 'ThrottleKnob', (.245, .965, cz - .03), (.025, .025, .025), .02, 'rubber')
    cable(mesh, 'ControlCable', [(.12, DECK + .02, cz + .2), (.18, DECK + .03, -1.6), (.12, DECK + .05, -1.95),
                                 (.06, section(TRANSOM)[0][1] - .02, TRANSOM + .02), (.04, section(TRANSOM)[0][1] + .1, -2.2)], .012, 'rubber')
    # The outboard on the transom: clamp, swivel, a cowled powerhead, the leg, the plate, the gearcase and its propeller.
    px, py, pz = PROP
    top = section(TRANSOM)[0][1]
    block(mesh, 'TransomClamp', (0, top - .02, TRANSOM - .02), (.20, .12, .09), 'steel', bevel=.004)
    for sx in (-1, 1):
        rod(mesh, 'ClampScrew', (sx * .065, top - .07, TRANSOM + .08), (sx * .065, top - .07, TRANSOM - .02), .011, 'steel', (10, 6, 4)[lod])
        soft(mesh, 'ClampPad', (sx * .065, top - .07, TRANSOM + .085), (.03, .03, .008), .006, 'rubber')
    block(mesh, 'Swivel', (0, top + .02, pz + .10), (.10, .14, .12), 'steel', bevel=.008)
    soft(mesh, 'Powerhead', (0, top + .21, pz - .03), (.12, .14, .19), .055, 'motor')
    soft(mesh, 'CowlTop', (0, top + .35, pz - .04), (.095, .025, .15), .02, 'motor')
    block(mesh, 'Midsection', (0, (top + .07 + .16) / 2, pz + .02), (.075, top + .07 - .16, .12), 'rubber', bevel=.012)
    block(mesh, 'Leg', (0, .10, pz + .02), (.06, .14, .11), 'steel', bevel=.01)
    block(mesh, 'CavitationPlate', (0, .17, pz + .02), (.22, .012, .26), 'steel', bevel=.003)
    gear_case = mesh.lathe_x('Gearcase', [(-.17, 0), (-.16, .035), (-.08, .05), (.10, .045), (.15, .02), (.16, 0)], (0, 0), 'steel',
                             segments=(16, 10, 6)[lod])
    place(gear_case, Matrix.Translation(SV((0, py, pz + .02))) @ Matrix.Rotation(math.pi / 2, 4, 'Z'))
    block(mesh, 'Skeg', (0, py - .08, pz + .03), (.012, .12, .12), 'steel', bevel=.002)
    hub = mesh.lathe_x('PropHub', [(-.04, 0), (-.04, .03), (.03, .025), (.05, 0)], (0, 0), 'steel', segments=(12, 8, 6)[lod])
    place(hub, Matrix.Translation(SV((0, py, pz - .17))) @ Matrix.Rotation(math.pi / 2, 4, 'Z'))
    for k in range(3):
        blade = mesh.box('PropBlade', (0, 0, 0), (.012, .05, .11), 'steel', .003)
        place(blade, Matrix.Translation(SV((0, py, pz - .17))) @ Matrix.Rotation(k * math.tau / 3, 4, 'Y') @
              Matrix.Translation((0, 0, .07)) @ Matrix.Rotation(math.radians(25), 4, 'Z'))
    rod(mesh, 'Tiller', (0, top + .14, pz + .18), (0, top + .12, pz + .40), .016, 'rubber', end=.013)
    # The fuel can on the deck by the transom, a hose to the motor.
    can = (.48, DECK + .18, -1.78)
    soft(mesh, 'FuelCan', can, (.09, .18, .16), .02, 'can')
    rod(mesh, 'CanHandle', (can[0], can[1] + .19, can[2] - .07), (can[0], can[1] + .19, can[2] + .07), .012, 'can')
    rod(mesh, 'CanCap', (can[0], can[1] + .17, can[2] + .1), (can[0], can[1] + .22, can[2] + .12), .018, 'rubber')
    cable(mesh, 'FuelHose', [(can[0], can[1] + .22, can[2] + .12), (.40, .55, -1.95), (.20, .82, TRANSOM + .05), (.06, .92, -2.2)],
          .009, 'rubber')
    # The crate lashed in the bow (the cargo), with a rope over it; a coil of rope and a cleat on the foredeck.
    crate_z = 1.42
    floor = section(crate_z)[-1][1]
    base = max(DECK, floor + .02)
    block(mesh, 'Crate', (0, base + .2, crate_z), (.62, .40, .44), 'crate', bevel=.008)
    block(mesh, 'CrateLid', (0, base + .41, crate_z), (.66, .03, .48), 'crate', bevel=.005)
    for sx in (-1, 1):
        block(mesh, 'CrateHandle', (sx * .325, base + .3, crate_z), (.02, .04, .18), 'steel', bevel=.003)
    cable(mesh, 'Lashing', [(.36, DECK + .01, crate_z), (.335, base + .43, crate_z), (-.335, base + .43, crate_z), (-.36, DECK + .01, crate_z)],
          .011, 'rope')
    # The foredeck: a plate under the gunwale from FOREDECK to the stem, its edges along the inside.
    zs = [FOREDECK + (BOW - .03 - FOREDECK) * k / (6, 4, 2)[lod] for k in range((6, 4, 2)[lod] + 1)]
    rows = [(max(section(z)[0][0] - TH - .002, .01), section(z)[0][1] - .045, z) for z in zs]
    orient(grid(mesh, 'Foredeck', [[(x, y, z), (-x, y, z)] for x, y, z in rows], 'paint', False), UP)
    orient(grid(mesh, 'ForedeckUnder', [[(-x, y - .02, z), (x, y - .02, z)] for x, y, z in rows], 'inner', False), DOWN)
    deck_at = section(LAMP_Z)[0][1] - .045
    if lod < 2:
        coil = [(.09 * math.cos(a), .09 * math.sin(a), 0) for a in np.linspace(0, math.tau, (18, 10, 6)[lod], endpoint=False)]
        for k in range(3):
            place(mesh.cable('RopeCoil', coil, .013, 'rope', closed=True, sides=(6, 4, 4)[lod]),
                  Matrix.Translation(SV((-.16, deck_at + .015 + k * .02, 1.97))) @ Matrix.Scale(1 - k * .12, 4))
    for z, x in ((2.12, .14), (-1.95, .55)):
        h = section(z)[0]
        block(mesh, 'Cleat', (x, (deck_at if z > 0 else h[1]) + .02, z), (.04, .035, .15), 'steel', bevel=.006)
    # The bow lamp on its post on the foredeck.
    lamp_at = (0, deck_at + .19, LAMP_Z)
    rod(mesh, 'LampPost', (0, deck_at, LAMP_Z - .03), (0, lamp_at[1] - .05, LAMP_Z - .01), .014, 'steel')
    block(mesh, 'LampFoot', (0, deck_at + .008, LAMP_Z - .03), (.08, .016, .08), 'steel', bevel=.003)
    lamp = mesh.lathe_x('LampHousing', [(-.07, 0), (-.07, .05), (.04, .065), (.06, .065), (.06, 0)], (0, 0), 'steel',
                        segments=(16, 10, 6)[lod])
    place(lamp, Matrix.Translation(SV(lamp_at)) @ Matrix.Rotation(math.pi / 2, 4, 'Z'))
    glass = mesh.lathe_x('LampGlass', [(.055, 0), (.055, .058), (.066, .058), (.066, 0)], (0, 0), 'glass', segments=(16, 10, 6)[lod])
    place(glass, Matrix.Translation(SV(lamp_at)) @ Matrix.Rotation(math.pi / 2, 4, 'Z'))
    LAMP_AT[:] = lamp_at
    # A spare paddle along the port side.
    if lod < 2:
        rod(mesh, 'Paddle', (.55, DECK + .04, -.9), (.48, DECK + .04, .9), .018, 'wood', (8, 6, 4)[lod])
        block(mesh, 'PaddleBlade', (.47, DECK + .03, 1.05), (.16, .012, .32), 'wood', yaw=math.radians(-3), bevel=.004)


objects = []
LAMP_AT = [0.0, 0.0, 0.0]
for lod in range(3):
    mesh = ComponentMesh(col, mat, regions, lod)
    hull(mesh)
    fittings(mesh)
    obj = mesh.finish(f'Body_lod{lod}')
    ao = vertex_ao([obj], samples=8, distance=.35, floor=True)[obj]
    colors = np.ones((len(ao), 4), np.float32); colors[:, :3] = (.55 + .45 * ao)[:, None]
    set_export_colour(obj, colors)
    objects.append(obj)
objects.append(convex_hull([objects[0]], 'Body_hull0-convcolonly', col, max_faces=64))
engine = bpy.data.objects.new('socket_engine_audio', None)
col.objects.link(engine)
engine.location = SV((0, section(TRANSOM)[0][1] + .21, PROP[2] - .03))
verts = [v.co for v in objects[0].data.vertices]
lo = [min(v[i] for v in verts) for i in range(3)]
hi = [max(v[i] for v in verts) for i in range(3)]
report = {'asset': 'motorboat', 'dimensions_blender_xyz_m': [h - l for h, l in zip(hi, lo)], 'min': lo, 'max': hi,
          'texture_size': n, 'material_count': 1,
          'lod_triangles': {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects[:3]},
          'hull_triangles': sum(len(p.vertices) - 2 for p in objects[-1].data.polygons), 'vertex_ao_samples': 8,
          'sockets': {'socket_engine_audio': list(engine.location)}, 'deck': DECK, 'seat': SEAT, 'wheel': WHEEL, 'prop': PROP,
          'lamp': list(LAMP_AT), 'transom_top': section(TRANSOM)[0][1], 'gunwale_mid': section(0)[0],
          'waterplane_note': 'ships.json buoyancy depth = gunwale amidships'}
export_glb(WORLD / 'motorboat.glb', objects + [engine], {'M_Hollow_Motorboat': mp})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'motorboat-source.blend'))
(OUT / 'geometry.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report), flush=True)


# ---------------------------------------------------------------- previews (look-dev only, never shipped)
def previews(prefix, holes=False):
    """Eevee look-dev renders; with `holes`, no water and a magenta sky, back faces culled as the game culls them, from
    every side, below and into the bow: magenta seen through the hull is a hole."""
    scene = bpy.context.scene
    for o in objects[1:]:
        o.hide_render = True
    shaded = bpy.data.materials.new('Preview')
    shaded.use_nodes = True
    nodes, links = shaded.node_tree.nodes, shaded.node_tree.links
    bsdf = next(nd for nd in nodes if nd.type == 'BSDF_PRINCIPLED')
    image = nodes.new('ShaderNodeTexImage'); image.image = bpy.data.images.load(str(paths['albedo']))
    attr = nodes.new('ShaderNodeVertexColor')
    mix = nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs[0].default_value = 1
    links.new(image.outputs['Color'], mix.inputs[6]); links.new(attr.outputs['Color'], mix.inputs[7])
    links.new(mix.outputs[2], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = .65
    shaded.use_backface_culling = True
    objects[0].data.materials[0] = shaded
    water = bpy.data.meshes.new('Water'); size = 30
    water.from_pydata([(-size, -size, .16), (size, -size, .16), (size, size, .16), (-size, size, .16)], [], [(0, 1, 2, 3)])
    sea = bpy.data.objects.new('Water', water); col.objects.link(sea)
    wm = bpy.data.materials.new('WaterPreview'); wm.use_nodes = True
    wb = next(nd for nd in wm.node_tree.nodes if nd.type == 'BSDF_PRINCIPLED')
    wb.inputs['Base Color'].default_value = (.03, .12, .14, 1); wb.inputs['Roughness'].default_value = .08
    water.materials.append(wm)
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); col.objects.link(sun)
    sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    world = bpy.data.worlds.new('Sky'); scene.world = world; world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (1, 0, 1, 1) if holes else (.55, .65, .75, 1)
    world.node_tree.nodes['Background'].inputs[1].default_value = .9
    if holes:
        sea.hide_render = True
    cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera')); col.objects.link(cam); scene.camera = cam
    cam.data.lens = 40
    try:
        scene.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError:
        scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = 1280, 800
    views = [('quarter', (5.5, 4.5, 3.0), (0, 0, .5)), ('side', (7.5, -.3, 1.0), (0, -.1, .5)),
             ('inside', (1.2, 3.6, 2.4), (0, -.6, .4)), ('helm', (0, 1.45, 1.50), (0, -2.5, .6)),
             ('stern', (2.6, 5.6, .9), (0, 2.2, .3))]
    if holes:
        views = [('bow-down', (0, -.9, 1.7), (0, -2.0, .3)), ('bow-in', (.25, .3, 1.05), (0, -2.2, .4)),
                 ('front', (0, -6.5, .9), (0, 0, .5)), ('front-low', (1.6, -5.5, .05), (0, -1.6, .45)),
                 ('below', (2.6, .5, -2.4), (0, 0, .3)), ('top', (0, .2, 7.5), (0, 0, .4)),
                 ('bow-quarter', (-4.0, -5.0, 2.6), (0, -1.0, .5)), ('stern-in', (0, -.5, 1.6), (0, 2.1, .4)),
                 ('side-far', (-7.5, .3, 1.0), (0, .1, .5))]
    for name, eye, target in views:
        cam.location = eye
        cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = f'{prefix}-{name}.png'
        bpy.ops.render.render(write_still=True)


args = script_args()
if '--preview' in args:
    previews(args[args.index('--preview') + 1])
if '--holes' in args:
    previews(args[args.index('--holes') + 1], holes=True)
