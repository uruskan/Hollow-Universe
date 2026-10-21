"""Original folded blueprint: a cyanotype sheet in three zig-zag panels, studied from Rust's public Blueprint icon.

The generic picture of a blueprint for the catalogue and item details; a stack that teaches an item still shows
that item's icon on blueprint blue (GroundUi.Paint.cs). One closed paper mesh per LOD with real thickness, an
original drawing (three views of an invented pump housing, a title block), no downloaded meshes or pixels.

    blender -b --factory-startup --python art/models/blueprint.py
"""
from pathlib import Path
import json, math, sys
import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from hollow import ITEMS, TEXTURES, REPO
from hollow.export import collection, export_glb, convex_hull, vertex_ao, set_export_colour
from hollow.godot import write_material
from hollow.textures import write_png, write_texture_import, normal_from_height
from hollow.component_mesh import ComponentMesh
from hollow.diagram_texture import Diagram

OUT = REPO / '.artifacts/itemart/blueprint'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('Blueprint')
# Top-down atlas regions (x, y, w, h): the printed face, the paper's back, its cut edges.
regions = {'print': (.01, .01, .98, .70), 'back': (.01, .73, .98, .19), 'edge': (.01, .94, .98, .05)}
W, H, THICK = .300, .210, .0006
FOLDS = (W / 3, 2 * W / 3)
n = 1024
v, u = np.mgrid[0:n, 0:n].astype(np.float32) / n
rng = np.random.default_rng(170170)
noise = rng.uniform(-1, 1, (n, n)).astype(np.float32)


def smooth_field(cells_weights, shape=(n, n)):
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


def window(name):
    x, y, w, h = regions[name]
    return slice(int(y * n), int((y + h) * n)), slice(int(x * n), int((x + w) * n))


albedo = np.ones((n, n, 4), np.float32)
orm = np.ones_like(albedo); orm[:, :, 2] = 0
height = noise * .0012
# The printed face: deep cyanotype blue, unevenly washed, paler where the folds and edges have rubbed.
ys, xs = window('print')
ph, pw = ys.stop - ys.start, xs.stop - xs.start
wash = smooth_field([(4, .6), (13, .3), (37, .12)], (ph, pw))
pv, pu = np.mgrid[0:ph, 0:pw].astype(np.float32)
pu /= pw - 1; pv /= ph - 1
sheet = np.zeros((ph, pw, 3), np.float32)
sheet[:] = np.array([.050, .135, .315])
sheet *= (.94 + wash * .10 + noise[ys, xs] * .02)[:, :, None]
d = Diagram(sheet)
ink = (.80, .88, .93)
faint = (.36, .52, .70)
# Margin and a fine construction grid.
for k in range(1, 30):
    d.line((k / 30, .035), (k / 30, .965), faint, .0005, .22)
for k in range(1, 21):
    d.line((.025, k / 21), (.975, k / 21), faint, .0005, .22)
d.box(.025, .035, .95, .93, color=ink, width=.0022)
d.box(.035, .05, .93, .90, color=ink, width=.0009)
# Front view: a pump housing with its inlet flange, outlet spout, foot and impeller bore.
cx, cy = .23, .44
d.circle(cx, cy, .115, .165, color=ink, width=.0024)
d.circle(cx, cy, .062, .089, color=ink, width=.0018)
d.circle(cx, cy, .020, .029, color=ink, width=.0016)
for k in range(8):
    a = k * math.tau / 8
    d.circle(cx + math.cos(a) * .088, cy + math.sin(a) * .126, .008, .011, color=ink, width=.0012)
d.path([(cx + .07, cy - .135), (cx + .07, cy - .30), (cx + .14, cy - .30), (cx + .14, cy - .11)], ink, .0022)
d.path([(cx - .14, cy + .14), (cx - .14, cy + .245), (cx + .14, cy + .245), (cx + .14, cy + .14)], ink, .0022)
d.line((cx - .20, cy), (cx + .20, cy), faint, .0008)
d.line((cx, cy - .26), (cx, cy + .22), faint, .0008)
# Side view, to the right, sharing the centre line.
sx = .55
d.box(sx - .055, cy - .165, .11, .33, color=ink, width=.0022)
d.box(sx - .075, cy - .19, .15, .025, color=ink, width=.0018)
d.box(sx - .075, cy + .165, .15, .08, color=ink, width=.0022)
for k in range(5):
    d.line((sx - .055, cy - .12 + k * .06), (sx + .055, cy - .12 + k * .06), ink, .0010, .8)
d.box(sx + .055, cy - .03, .065, .06, color=ink, width=.0018)
d.line((sx - .12, cy), (sx + .16, cy), faint, .0008)
# Plan view and an exploded bolt, upper right.
d.circle(.83, .25, .085, .12, color=ink, width=.0020)
d.circle(.83, .25, .035, .05, color=ink, width=.0016)
d.path([(.76, .40), (.90, .40), (.90, .46), (.76, .46)], ink, .0016, closed=True)
d.line((.83, .50), (.83, .62), ink, .0016)
d.path([(.815, .50), (.845, .50), (.845, .52), (.815, .52)], ink, .0012, closed=True)
# Dimensions with arrows, and leader lines to callout circles.
d.dimension((.115, .055 + .02), (.345, .055 + .02), color=ink, width=.0010)
d.dimension((.075, cy - .165), (.075, cy + .165), color=ink, width=.0010)
d.dimension((sx - .055, .90), (sx + .055, .90), color=ink, width=.0010)
for (a, b, c) in [((.30, .32), (.40, .20), (.42, .18)), ((.60, .55), (.68, .66), (.70, .68)), ((.88, .30), (.94, .37), (.945, .39))]:
    d.path([a, b], ink, .0009)
    d.circle(c[0], c[1], .014, .02, color=ink, width=.0011)
# Title block, bottom right: ruled boxes and abstract marks, no lettering.
d.box(.66, .78, .30, .17, color=ink, width=.0018)
for k in range(1, 4):
    d.line((.66, .78 + k * .0425), (.96, .78 + k * .0425), ink, .0010)
d.line((.78, .78), (.78, .95), ink, .0010)
for row in range(4):
    for k in range(3 + row % 2):
        x0 = .795 + k * .045
        d.line((x0, .80 + row * .0425), (x0 + .028 - (k + row) % 3 * .006, .80 + row * .0425), ink, .0016, .9)
    d.line((.672, .80 + row * .0425), (.765 - row * .01, .80 + row * .0425), ink, .0016, .9)
# Folds and handling: paler creases where the sheet folds, rubbed edges and a few soft stains.
rub = np.zeros((ph, pw), np.float32)
for f in FOLDS:
    rub += np.exp(-((pu - f / W) / .006) ** 2) * (.55 + .45 * np.clip(smooth_field([(23, 1)], (ph, pw)) + .5, 0, 1))
edge = np.minimum.reduce([pu, 1 - pu, pv, 1 - pv])
rub += np.exp(-(edge / .01) ** 2) * (.6 + .4 * noise[ys, xs])
stains = np.clip(smooth_field([(7, 1), (19, .4)], (ph, pw)) - .55, 0, 1) * 1.6
rub = np.clip(rub, 0, 1)
sheet = sheet * (1 - rub[:, :, None] * .45) + np.array([.62, .71, .80]) * rub[:, :, None] * .45
sheet *= (1 - stains * .22)[:, :, None]
albedo[ys, xs, :3] = sheet
orm[ys, xs, 1] = .74 + rub * .1
height[ys, xs] += wash * .004 - np.exp(-((pu[:, :] - FOLDS[0] / W) / .004) ** 2) * .02 - np.exp(-((pu - FOLDS[1] / W) / .004) ** 2) * .02
# The back: cyanotype paper's paler blue-grey, with a ghost of the print through it.
ys, xs = window('back')
bh, bw = ys.stop - ys.start, xs.stop - xs.start
fibre = smooth_field([(200, .5)], (bh, bw)) + noise[ys, xs] * .3
albedo[ys, xs, :3] = np.array([.56, .62, .69]) * (.95 + fibre * .05 + smooth_field([(5, .5)], (bh, bw)) * .06)[:, :, None]
orm[ys, xs, 1] = .86
height[ys, xs] += fibre * .006
ys, xs = window('edge')
albedo[ys, xs, :3] = np.array([.70, .74, .78]) * (.96 + noise[ys, xs] * .03)[:, :, None]
orm[ys, xs, 1] = .9
paths = {}
for key, pixels in [('albedo', np.clip(albedo, 0, 1)), ('orm', np.clip(orm, 0, 1)), ('normal', normal_from_height(height, 1.6))]:
    paths[key] = write_png(TEXTURES / 'blueprint' / f'blueprint_{key}.png', pixels)
    write_texture_import(paths[key], normal_map=key == 'normal')
mp = write_material('blueprint', 'M_Hollow_Blueprint', albedo=paths['albedo'], orm=paths['orm'], normal=paths['normal'],
                    normal_scale=.4, vertex_colour=True, vertex_colour_srgb=False)
mat = bpy.data.materials.new('M_Hollow_Blueprint'); mat.diffuse_color = (.1, .22, .45, 1)


def fold_angle(s):
    """The sheet's slope along its length: up, down, up again, with folds rounded over a few millimetres."""
    def step(x, at, width=.0035):
        t = min(1.0, max(0.0, (x - at + width) / (2 * width)))
        return t * t * (3 - 2 * t)
    first, second, third = math.radians(27), math.radians(-27.5), math.radians(19)
    return first + (second - first) * step(s, FOLDS[0]) + (third - second) * step(s, FOLDS[1])


def centreline(samples):
    """(x, z) of the folded sheet's middle at each length s, integrated from its slope."""
    fine = np.linspace(0, W, 3001)
    x, z = [0.0], [0.0]
    for a, b in zip(fine[:-1], fine[1:]):
        angle = fold_angle((a + b) / 2)
        x.append(x[-1] + (b - a) * math.cos(angle)); z.append(z[-1] + (b - a) * math.sin(angle))
    return np.interp(samples, fine, x), np.interp(samples, fine, z)


def length_samples(lod):
    base = (30, 14, 8)[lod]
    s = set(np.round(np.linspace(0, W, base + 1), 6))
    for f in FOLDS:
        for k in range(-(3, 2, 1)[lod], (3, 2, 1)[lod] + 1):
            s.add(round(f + k * .0035 / max(1, (3, 2, 1)[lod]), 6))
    return np.array(sorted(s))


objects = []
for lod in range(3):
    mesh = ComponentMesh(col, mat, regions, lod)
    s_all = length_samples(lod)
    t_all = np.linspace(0, H, (12, 6, 3)[lod] + 1)
    cxs, czs = centreline(s_all)
    ns, nt = len(s_all), len(t_all)

    def surface(i, j):
        s, t = s_all[i], t_all[j]
        # A gentle sag across the panels, the last panel's free corner lifting, a slight wave along the folds.
        sag = .0025 * math.sin(math.pi * t / H) * (1 - abs(math.sin(math.pi * 3 * s / W)))
        lift = .009 * max(0.0, (s - FOLDS[1]) / (W - FOLDS[1])) ** 2 * (1 - t / H)
        wave = .0012 * math.sin(t / H * 5.1 + s * 9) * math.sin(math.pi * s / W * 3) ** 2
        return Vector((cxs[i], t, czs[i] - sag + lift + wave))

    top = [[surface(i, j) for j in range(nt)] for i in range(ns)]
    normals = []
    for i in range(ns):
        row = []
        for j in range(nt):
            a = top[min(i + 1, ns - 1)][j] - top[max(i - 1, 0)][j]
            b = top[i][min(j + 1, nt - 1)] - top[i][max(j - 1, 0)]
            normal = a.cross(b).normalized()
            row.append(normal if normal.z > 0 else -normal)
        normals.append(row)
    vertices, faces, kind = [], [], []
    for layer in (0, 1):
        for i in range(ns):
            for j in range(nt):
                p = top[i][j] - normals[i][j] * (THICK * layer)
                vertices.append(tuple(p))
    grid = lambda layer, i, j: layer * ns * nt + i * nt + j
    for layer in (0, 1):
        for i in range(ns - 1):
            for j in range(nt - 1):
                faces.append((grid(layer, i, j), grid(layer, i + 1, j), grid(layer, i + 1, j + 1), grid(layer, i, j + 1)))
                kind.append('print' if layer == 0 else 'back')
    for i in range(ns - 1):
        for j in (0, nt - 1):
            faces.append((grid(0, i, j), grid(0, i + 1, j), grid(1, i + 1, j), grid(1, i, j))); kind.append('edge')
    for j in range(nt - 1):
        for i in (0, ns - 1):
            faces.append((grid(0, i, j), grid(0, i, j + 1), grid(1, i, j + 1), grid(1, i, j))); kind.append('edge')
    obj = mesh.mesh('FoldedSheet', vertices, faces, 'print', True)
    # Each face set reads its own region: the print and back by the sheet's unfolded length and width.
    uv = obj.data.uv_layers.active
    for face, k in zip(obj.data.polygons, kind):
        x0, y0, w, h = regions[k]
        face.use_smooth = k != 'edge'
        for loop in face.loop_indices:
            index = obj.data.loops[loop].vertex_index % (ns * nt)
            i, j = divmod(index, nt)
            fu, fv = s_all[i] / W, t_all[j] / H
            if k == 'back':
                fu = 1 - fu
            uv.data[loop].uv = (x0 + (.005 + .99 * fu) * w, 1 - y0 - (.005 + .99 * fv) * h)
    obj = mesh.finish(f'Blueprint_lod{lod}')
    ao = vertex_ao([obj], samples=8, distance=.05, floor=True)[obj]
    colors = np.ones((len(ao), 4), np.float32); colors[:, :3] = (.70 + .30 * ao)[:, None]
    set_export_colour(obj, colors)
    objects.append(obj)
objects.append(convex_hull([objects[0]], 'Blueprint_hull0-convcolonly', col, max_faces=40))
# Pickup pivot at the bottom, the sheet centred over it.
verts = [v.co for v in objects[0].data.vertices]
centre = Vector(((max(v.x for v in verts) + min(v.x for v in verts)) / 2, (max(v.y for v in verts) + min(v.y for v in verts)) / 2,
                 min(v.z for v in verts)))
for obj in objects:
    for vertex in obj.data.vertices:
        vertex.co -= centre
verts = [v.co for v in objects[0].data.vertices]
report = {'asset': 'blueprint', 'dimensions_blender_xyz_m': [max(v[i] for v in verts) - min(v[i] for v in verts) for i in range(3)],
          'unfolded_sheet_m': [W, H], 'paper_thickness_m': THICK, 'texture_size': n, 'material_count': 1,
          'lod_triangles': {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects[:3]},
          'hull_triangles': sum(len(p.vertices) - 2 for p in objects[-1].data.polygons), 'vertex_ao_samples': 8}
export_glb(ITEMS / 'blueprint.glb', objects, {'M_Hollow_Blueprint': mp})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'blueprint-source.blend'))
(OUT / 'geometry.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report), flush=True)
