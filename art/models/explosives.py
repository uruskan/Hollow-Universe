"""Original packaged explosive compound, studied from Rust's public Explosives icon.

Three soft off-white blocks of a fictional industrial compound: two bound side by side in a printed kraft band, the
third laid across them in a plain band, each band tied with twine. No downloaded meshes or pixels; one atlas and
material, hand-built LODs and a small deterministic vertex-occlusion pass.

    blender -b --factory-startup --python art/models/explosives.py
"""
from pathlib import Path
import json, math, sys
import bpy
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from hollow import ITEMS, TEXTURES, REPO
from hollow.export import collection, export_glb, convex_hull, vertex_ao, set_export_colour
from hollow.godot import write_material
from hollow.textures import write_png, write_texture_import, normal_from_height
from hollow.component_mesh import ComponentMesh
from hollow.rounded import rounded_box, sleeve, profile_loop, weld

OUT = REPO / '.artifacts/itemart/explosives'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('Explosives')
# Top-down atlas regions (x, y, w, h).
regions = {'putty': (.01, .01, .98, .44), 'paper': (.01, .47, .98, .20),
           'label': (.01, .69, .98, .20), 'cord': (.01, .91, .98, .08)}
n = 1024
v, u = np.mgrid[0:n, 0:n].astype(np.float32) / n
rng = np.random.default_rng(120120)
noise = rng.uniform(-1, 1, (n, n)).astype(np.float32)


def smooth_field(cells_weights):
    field = np.zeros((n, n), np.float32)
    for cells, weight in cells_weights:
        grid = rng.uniform(-1, 1, (cells + 1, cells + 1)).astype(np.float32)
        xx, yy = u * cells, v * cells; ix = xx.astype(int); iy = yy.astype(int)
        fx, fy = xx - ix, yy - iy; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
        field += ((grid[iy, ix] * (1 - fx) + grid[iy, ix + 1] * fx) * (1 - fy)
                  + (grid[iy + 1, ix] * (1 - fx) + grid[iy + 1, ix + 1] * fx) * fy) * weight
    return field


cloud = smooth_field([(5, .7), (16, .35), (43, .16), (121, .08)])
fibre = smooth_field([(260, .6)]) + noise * .25
albedo = np.ones((n, n, 4), np.float32)
orm = np.ones_like(albedo)
height = np.zeros((n, n), np.float32)
# Putty: a dense, slightly waxy off-white compound with grey mottling, pores and handling smudges.
putty = v < .46
pores = (noise > .93).astype(np.float32)
grime = np.clip(smooth_field([(9, .8), (31, .4)]) * 1.4 - .25, 0, 1)
tone = .88 + cloud * .06 + noise * .02 - pores * .10 - grime * .07
albedo[putty, :3] = (np.array([.70, .69, .62]) * tone[:, :, None])[putty]
orm[putty, 1] = (.66 + cloud * .08 + pores * .2 + grime * .08)[putty]; orm[putty, 2] = 0
height[putty] = (cloud * .07 + noise * .008 - pores * .035)[putty]
# Kraft paper: tan, fibrous, softly creased.
kraft_tone = (.92 + cloud * .10 + fibre * .05)[:, :, None]
crease = np.abs(np.sin((u * 3.1 + v * 7.3 + cloud * 2.2) * math.tau)) ** 24
paper = (v >= .46) & (v < .68)
albedo[paper, :3] = (np.array([.56, .42, .26]) * kraft_tone)[paper]
orm[paper, 1] = .86; orm[paper, 2] = 0
height[paper] = (fibre * .010 + cloud * .02 - crease * .03)[paper]
# The printed band: kraft with a broad red-orange ring of ink and black chevrons running round it, no lettering.
label = (v >= .68) & (v < .90)
albedo[label, :3] = (np.array([.56, .42, .26]) * kraft_tone)[label]
lv = (v - .69) / .20
ink = (u > .28) & (u < .72)
chevron = ((np.abs(((lv * 9 + np.abs(u - .5) * 6) % 1) - .5) < .17) & (np.abs(u - .5) < .13))
edge = (np.abs(u - .28) < .012) | (np.abs(u - .72) < .012)
wear = np.clip(cloud * 1.6 + noise * .4, 0, 1) > .78
albedo[label & ink, :3] = (np.array([.70, .19, .06]) * (.93 + cloud * .10)[:, :, None])[label & ink]
albedo[label & chevron, :3] = np.array([.06, .055, .05])
albedo[label & edge, :3] = np.array([.06, .055, .05])
albedo[label & ink & wear, :3] = (np.array([.56, .42, .26]) * kraft_tone)[label & ink & wear] * .95
orm[label, 1] = .82; orm[label & ink, 1] = .70; orm[label, 2] = 0
height[label] = (fibre * .010 + cloud * .02 - crease * .03)[label]
# Twine: twisted plies along the strand (u), round the strand (v).
cord = v >= .90
cv = (v - .91) / .08
twist = np.sin((u * 140 + cv * 3) * math.tau) * .5 + .5
albedo[cord, :3] = (np.array([.46, .38, .26]) * (.78 + twist * .30 + noise * .04)[:, :, None])[cord]
orm[cord, 1] = .9; orm[cord, 2] = 0
height[cord] = (twist * .06)[cord]
orm[:, :, 0] = 1
paths = {}
for key, pixels in [('albedo', np.clip(albedo, 0, 1)), ('orm', np.clip(orm, 0, 1)), ('normal', normal_from_height(height, 2.0))]:
    paths[key] = write_png(TEXTURES / 'explosives' / f'explosives_{key}.png', pixels)
    write_texture_import(paths[key], normal_map=key == 'normal')
mp = write_material('explosives', 'M_Hollow_Explosives', albedo=paths['albedo'], orm=paths['orm'], normal=paths['normal'],
                    normal_scale=.45, vertex_colour=True, vertex_colour_srgb=False)
mat = bpy.data.materials.new('M_Hollow_Explosives'); mat.diffuse_color = (.8, .78, .7, 1)


def place(obj, matrix):
    for vertex in obj.data.vertices:
        vertex.co = matrix @ vertex.co
    obj.data.update()
    return obj


def presses(seed, half, keep_clear):
    """Soft handling dents on a block's faces: inward only, none under its band (|x| < keep_clear)."""
    r = np.random.default_rng(seed)
    marks = [(Vector((r.uniform(-half[0], half[0]), r.uniform(-half[1], half[1]), half[2])), r.uniform(.008, .016),
              r.uniform(.0022, .0036)) for _ in range(4)]
    marks += [(Vector((r.uniform(-half[0], half[0]), half[1] * s, r.uniform(-half[2], half[2]))), r.uniform(.007, .013),
               r.uniform(.0010, .0022)) for s in (-1, 1) for _ in range(2)]
    marks = [m for m in marks if abs(m[0].x) > keep_clear + m[1]]
    phase = r.uniform(0, math.tau, 3)

    def displace(p, normal):
        p = Vector(p)
        k = -.0007 * (1 + math.sin(p.x * 61 + phase[0]) * math.sin(p.y * 73 + phase[1]) * math.sin(p.z * 89 + phase[2]))
        if abs(p.x) > keep_clear:
            for at, radius, depth in marks:
                d = (p - at).length
                if d < 2.5 * radius:
                    k -= depth * math.exp(-(d / radius) ** 2)
        # A soft compound sags a little on its long sides.
        k += .0009 * (1 - (p.z / half[2]) ** 2) * abs(normal[1]) - .0004
        return min(k, 0.0)
    return displace


def tie(mesh, name, x, half_y, half_z, radius, corner, thickness, knot_at):
    """Twine round a band at x, with a small bow on top at knot_at (y)."""
    sides = (6, 5, 4)[mesh.lod]
    loop = profile_loop(half_y, half_z, radius, corner)
    gap = thickness + .0004 + .0014
    points = [(x, y + ny * gap, z + nz * gap) for y, z, ny, nz in loop]
    mesh.cable(name, points, .0014, 'cord', closed=True, sides=sides)
    top = half_z + gap + .0012
    steps = (14, 10, 6)[mesh.lod]
    for side in (-1, 1):
        bow = [(x + side * (.004 + .010 * (1 - math.cos(a)) * .5), knot_at + .007 * math.sin(a) * side, top + .0018 * math.sin(a / 2))
               for a in np.linspace(0, math.tau, steps, endpoint=False)]
        mesh.cable(f'{name}Bow', bow, .0012, 'cord', closed=True, sides=sides)
        tail = [(x + side * .003, knot_at, top), (x + side * .009, knot_at - .010, top - .0008),
                (x + side * .012, knot_at - .019, top - .003)]
        mesh.cable(f'{name}Tail', tail, .0012, 'cord', closed=False, sides=sides)
    knot = [(x + .0028 * math.cos(a), knot_at + .0028 * math.sin(a), top) for a in np.linspace(0, math.tau, steps // 2 + 2, endpoint=False)]
    mesh.cable(f'{name}Knot', knot, .0018, 'cord', closed=True, sides=sides)


objects = []
for lod in range(3):
    mesh = ComponentMesh(col, mat, regions, lod)
    corner, flat = (4, 3, 2)[lod], ((6, 3, 2), (3, 2, 1), (2, 1, 1))[lod]
    band_corner = (5, 3, 2)[lod]
    # The bound pair, side by side; their shared band holds them as one.
    half = (.080, .040, .027); radius = .009
    pair = Matrix.Rotation(math.radians(-7), 4, 'Z')
    for i, (y, seed) in enumerate([(-.0412, 11), (.0412, 23)]):
        vertices, faces = rounded_box(half, radius, corner, flat, presses(seed, half, .024))
        block = mesh.mesh(f'BoundBlock{i}', vertices, faces, 'putty', True)
        weld(block)
        place(block, pair @ Matrix.Translation((0, y, half[2])))
    band_half = (.0412 + half[1], half[2])
    vertices, faces = sleeve(-.023, .023, band_half[0], band_half[1], radius, band_corner, .0007, gap=.0003, slices=(4, 2, 1)[lod])
    band = mesh.mesh('PrintedBand', vertices, faces, 'label', True)
    place(band, pair @ Matrix.Translation((0, 0, half[2])))
    first = len(mesh.parts)
    tie(mesh, 'PairTwine', .0, band_half[0], band_half[1], radius, band_corner, .0007, .022)
    for part in mesh.parts[first:]:
        place(part, pair @ Matrix.Translation((0, 0, half[2])))
    # The third block lies across the pair beyond the band, in a plain band and its own twine.
    top_half = (.071, .035, .024)
    vertices, faces = rounded_box(top_half, .0085, corner, flat, presses(37, top_half, .016))
    top_block = mesh.mesh('LooseBlock', vertices, faces, 'putty', True)
    weld(top_block)
    across = pair @ Matrix.Translation((.062, .004, 2 * half[2] + top_half[2] - .0012)) @ Matrix.Rotation(math.radians(93), 4, 'Z')
    place(top_block, across)
    first = len(mesh.parts)
    vertices, faces = sleeve(-.016, .016, top_half[1], top_half[2], .0085, band_corner, .0007, gap=.0003, slices=(3, 2, 1)[lod])
    mesh.mesh('PlainBand', vertices, faces, 'paper', True)
    tie(mesh, 'LooseTwine', .0, top_half[1], top_half[2], .0085, band_corner, .0007, -.010)
    for part in mesh.parts[first:]:
        place(part, across)
    obj = mesh.finish(f'Explosives_lod{lod}')
    ao = vertex_ao([obj], samples=8, distance=.06, floor=True)[obj]
    colors = np.ones((len(ao), 4), np.float32); colors[:, :3] = (.62 + .38 * ao)[:, None]
    set_export_colour(obj, colors)
    objects.append(obj)
objects.append(convex_hull([objects[0]], 'Explosives_hull0-convcolonly', col, max_faces=40))
bottom = min(v.co.z for v in objects[0].data.vertices)
for obj in objects:
    for vertex in obj.data.vertices:
        vertex.co.z -= bottom
vertices = [v.co for v in objects[0].data.vertices]
report = {'asset': 'explosives', 'dimensions_blender_xyz_m': [max(v[i] for v in vertices) - min(v[i] for v in vertices) for i in range(3)],
          'texture_size': n, 'material_count': 1,
          'lod_triangles': {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects[:3]},
          'hull_triangles': sum(len(p.vertices) - 2 for p in objects[-1].data.polygons), 'vertex_ao_samples': 8}
export_glb(ITEMS / 'explosives.glb', objects, {'M_Hollow_Explosives': mp})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'explosives-source.blend'))
(OUT / 'geometry.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report), flush=True)
