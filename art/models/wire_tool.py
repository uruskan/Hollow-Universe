"""Original Wire Tool: a handheld wiring probe with a wire reel, reviewed against Rust's public Wire Tool icon.

Replaces the primitive probe (four boxes and a cone baked by the removed WireToolAssetBuilder) in the same frame, so
its held presentation (held-presentations.json) still fits: the length runs up Blender +Z (Godot +Y), the screen
faces Blender -Y (Godot +Z), the right hand's grip is at the origin (socket_grip_r) and the probe's point at
0.275 m (socket_wire_tip, the presentation's contact socket). No downloaded meshes or pixels; one atlas and
material with an emissive screen, hand-built LODs and a small deterministic vertex-occlusion pass.

    blender -b --factory-startup --python art/models/wire_tool.py
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
from hollow.rounded import rounded_box, weld

OUT = REPO / '.artifacts/itemart/wire-tool'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('WireTool')
TIP = .275
regions = {'rubber': (.01, .01, .48, .40), 'shell': (.51, .01, .48, .40), 'screen': (.01, .43, .30, .20),
           'steel': (.33, .43, .32, .20), 'brass': (.67, .43, .32, .20), 'copper': (.01, .65, .48, .16),
           'wire': (.51, .65, .48, .16), 'button': (.01, .83, .30, .16), 'mark': (.33, .83, .66, .16)}
n = 1024
rng = np.random.default_rng(238238)


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
emission = np.zeros((n, n, 4), np.float32); emission[:, :, 3] = 1


def region(name, colour, rough, metal, cells=((6, .5), (23, .2)), variation=.07):
    ys, xs = window(name)
    shape = (ys.stop - ys.start, xs.stop - xs.start)
    cloud = smooth_field(shape, cells)
    noise = rng.uniform(-1, 1, shape).astype(np.float32)
    albedo[ys, xs, :3] = np.array(colour) * (1 + cloud * variation + noise * .02)[:, :, None]
    orm[ys, xs, 1] = rough + cloud * .04; orm[ys, xs, 2] = metal
    height[ys, xs] = cloud * .015 + noise * .004
    vv, uu = np.mgrid[0:shape[0], 0:shape[1]].astype(np.float32)
    return ys, xs, cloud, noise, uu / shape[1], vv / shape[0]


# Grip rubber: charcoal with a fine stipple for grip.
ys, xs, cloud, noise, uu, vv = region('rubber', (.075, .075, .08), .86, 0)
stipple = (np.sin(uu * 420) * np.sin(vv * 330) > .6).astype(np.float32)
height[ys, xs] += stipple * .03
albedo[ys, xs, :3] *= (1 - stipple * .15)[:, :, None]
# The housing: safety-yellow paint, scuffed to grey plastic at its corners, black hazard band at the bottom.
ys, xs, cloud, noise, uu, vv = region('shell', (.78, .58, .10), .48, 0)
scuff = np.clip(smooth_field(cloud.shape, [(40, 1), (120, .5)]) + cloud * .4 - .7, 0, 1) * 3
scuff = np.clip(scuff, 0, 1)
albedo[ys, xs, :3] = albedo[ys, xs, :3] * (1 - scuff[:, :, None]) + np.array([.32, .31, .29]) * scuff[:, :, None]
orm[ys, xs, 1] += scuff * .2
# The screen: dark glass with a bright trace and a reading bar, lit (emission) where it draws.
ys, xs, cloud, noise, uu, vv = region('screen', (.02, .05, .05), .12, 0, variation=.02)
trace = np.exp(-((vv - .45 - .16 * np.sin(uu * 19) * np.exp(-((uu - .5) / .22) ** 2)) / .025) ** 2) * (uu > .08) * (uu < .92)
grid = ((np.abs(((uu * 10) % 1) - .5) > .47) | (np.abs(((vv * 6) % 1) - .5) > .46)) * .25
bar = ((vv > .78) & (vv < .88) & (uu > .08) & (uu < .08 + .62)).astype(np.float32)
lit = np.clip(trace + grid + bar * .8, 0, 1)
frame = np.minimum.reduce([uu, 1 - uu, vv, 1 - vv]) < .04
lit[frame] = 0
albedo[ys, xs, :3] += lit[:, :, None] * np.array([.10, .45, .38])
emission[ys, xs, :3] = lit[:, :, None] * np.array([.12, .85, .70])
# Steel, brass, the reel's copper winding, red insulation, a red button, black marks.
ys, xs, cloud, noise, uu, vv = region('steel', (.55, .55, .54), .32, 1)
albedo[ys, xs, :3] *= (1 + np.sin(vv * 900) * .03)[:, :, None]
region('brass', (.66, .50, .24), .34, 1)
ys, xs, cloud, noise, uu, vv = region('copper', (.62, .32, .16), .36, 1)
turns = np.sin(uu * 2 * math.pi * 60) * .5 + .5
albedo[ys, xs, :3] *= (.75 + turns * .35)[:, :, None]
height[ys, xs] += turns * .05
region('wire', (.55, .07, .05), .55, 0)
region('button', (.62, .10, .07), .6, 0)
ys, xs, cloud, noise, uu, vv = region('mark', (.78, .58, .10), .5, 0)
stripe = ((uu * 9 + vv * 1.6) % 1) < .5
albedo[ys, xs, :3][stripe] = np.array([.05, .05, .05])
paths = {}
for key, pixels in [('albedo', np.clip(albedo, 0, 1)), ('orm', np.clip(orm, 0, 1)), ('normal', normal_from_height(height, 2.0)),
                    ('emission', np.clip(emission, 0, 1))]:
    paths[key] = write_png(TEXTURES / 'wire-tool' / f'wire-tool_{key}.png', pixels)
    write_texture_import(paths[key], normal_map=key == 'normal')
mp = write_material('wire-tool', 'M_Hollow_WireTool', albedo=paths['albedo'], orm=paths['orm'], normal=paths['normal'],
                    emission=paths['emission'], emission_energy=1.4, normal_scale=.5, vertex_colour=True, vertex_colour_srgb=False)
mat = bpy.data.materials.new('M_Hollow_WireTool'); mat.diffuse_color = (.7, .55, .12, 1)


def place(obj, matrix):
    for vertex in obj.data.vertices:
        vertex.co = matrix @ vertex.co
    obj.data.update()
    return obj


def toward(a, b):
    a, b = Vector(a), Vector(b)
    q = (b - a).normalized().to_track_quat('X', 'Y')
    return Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()


def rod(mesh, name, a, b, radius, region, end=None, segments=None):
    length = (Vector(b) - Vector(a)).length
    rings = [(-length / 2, 0), (-length / 2, radius), (length / 2, radius if end is None else end), (length / 2, 0)]
    obj = mesh.lathe_x(name, rings, (0, 0), region, segments=segments or (20, 12, 8)[mesh.lod])
    return place(obj, toward(a, b))


def lathe_z(mesh, name, rings, region, segments=None):
    """A profile of (z, radius) rings turned round the tool's length."""
    obj = mesh.lathe_x(name, rings, (0, 0), region, segments=segments or (24, 14, 8)[mesh.lod])
    return place(obj, Matrix.Rotation(-math.pi / 2, 4, 'Y'))


def soft_box(mesh, name, half, radius, region, centre, displace=None):
    corner, flat = (4, 3, 2)[mesh.lod], ((3, 2, 4), (2, 1, 2), (1, 1, 1))[mesh.lod]
    vertices, faces = rounded_box(half, radius, corner, flat, displace)
    obj = mesh.mesh(name, vertices, faces, region, True)
    weld(obj)
    return place(obj, Matrix.Translation(centre))


def grooves(p, normal):
    """Finger grooves down the grip's front, and a slight swell at the palm on its back."""
    x, y, z = p
    k = 0.0
    if normal[1] < -.3 and -.06 < z < .05:
        k -= .0028 * math.sin((z + .06) / .11 * 3 * math.pi) ** 2 * min(1.0, -normal[1] * 1.6)
    if normal[1] > .3:
        k += .0025 * math.cos(z / .085 * math.pi / 2) ** 2 * normal[1]
    return k


objects = []
for lod in range(3):
    mesh = ComponentMesh(col, mat, regions, lod)
    soft_box(mesh, 'Grip', (.025, .023, .086), .016, 'rubber', (0, 0, 0), grooves)
    soft_box(mesh, 'GripCollar', (.030, .027, .007), .006, 'shell', (0, 0, .090))
    soft_box(mesh, 'Housing', (.047, .029, .040), .012, 'shell', (0, 0, .135))
    soft_box(mesh, 'HazardBand', (.0475, .0295, .006), .011, 'mark', (0, 0, .101))
    soft_box(mesh, 'Bumper', (.050, .031, .006), .010, 'rubber', (0, 0, .172))
    # The screen in a rubber bezel on the front, two buttons below it.
    mesh.box('Bezel', (0, -.0296, .144), (.070, .0028, .044), 'rubber', .0012)
    mesh.box('Screen', (0, -.0312, .145), (.062, .0012, .036), 'screen', 0)
    for x, region_name in ((-.016, 'button'), (.016, 'rubber')):
        obj = mesh.lathe_x('Button', [(-.002, 0), (-.002, .0062), (.002, .0055), (.0032, .0045), (.0032, 0)], (0, 0), region_name,
                           segments=(14, 10, 6)[lod])
        place(obj, Matrix.Translation((x, -.030, .110)) @ Matrix.Rotation(-math.pi / 2, 4, 'Z'))
    # The nozzle, its brass collar and the probe, tapering to its point at the tip socket.
    lathe_z(mesh, 'Nozzle', [(.174, 0), (.174, .014), (.186, .013), (.204, .0085), (.206, 0)], 'steel')
    lathe_z(mesh, 'Collar', [(.194, 0), (.194, .0118), (.200, .0118), (.200, 0)], 'brass')
    lathe_z(mesh, 'Probe', [(.205, 0), (.205, .0036), (.262, .0028), (.268, .0024), (.268, 0)], 'steel', (12, 8, 6)[lod])
    lathe_z(mesh, 'ProbePoint', [(.267, 0), (.267, .0026), (TIP, .0003), (TIP, 0)], 'brass', (12, 8, 6)[lod])
    # The wire reel on the right side: two rims, the copper winding, its axle cap.
    reel_x, reel_z = .047, .135
    for x0, x1 in ((reel_x, reel_x + .003), (reel_x + .019, reel_x + .022)):
        rod(mesh, 'ReelRim', (x0, 0, reel_z), (x1, 0, reel_z), .027, 'steel', segments=(28, 16, 10)[lod])
    rod(mesh, 'ReelWinding', (reel_x + .003, 0, reel_z), (reel_x + .019, 0, reel_z), .021, 'copper', segments=(28, 16, 10)[lod])
    rod(mesh, 'ReelAxle', (reel_x + .022, 0, reel_z), (reel_x + .026, 0, reel_z), .007, 'brass', segments=(12, 8, 6)[lod])
    # Red insulated lead from the reel's top round to the nozzle, where it feeds the probe.
    lead = [(reel_x + .011, -.004, reel_z + .021), (reel_x + .010, -.010, reel_z + .034), (.040, -.014, .168),
            (.026, -.016, .186), (.014, -.012, .193), (.0095, -.008, .197)]
    mesh.cable('Lead', lead, .0022, 'wire', closed=False, sides=(8, 6, 4)[lod])
    # A belt clip on the back and a lanyard loop under the grip.
    mesh.box('BeltClip', (0, .0315, .128), (.022, .003, .062), 'steel', .001)
    mesh.box('ClipFoot', (0, .0300, .162), (.022, .006, .008), 'steel', .001)
    loop = [(.0, .0 + .010 * math.cos(a), -.094 - .010 * math.sin(a)) for a in np.linspace(0, math.tau, (16, 10, 8)[lod], endpoint=False)]
    mesh.cable('LanyardLoop', loop, .0022, 'steel', closed=True, sides=(6, 5, 4)[lod])
    obj = mesh.finish(f'WireTool_lod{lod}')
    ao = vertex_ao([obj], samples=8, distance=.05, floor=False)[obj]
    colors = np.ones((len(ao), 4), np.float32); colors[:, :3] = (.62 + .38 * ao)[:, None]
    set_export_colour(obj, colors)
    objects.append(obj)
objects.append(convex_hull([objects[0]], 'WireTool_hull0-convcolonly', col, max_faces=48))
sockets = []
for name, at in (('socket_grip_r', (0, 0, 0)), ('socket_wire_tip', (0, 0, TIP))):
    e = bpy.data.objects.new(name, None); col.objects.link(e); e.location = at; sockets.append(e)
verts = [v.co for v in objects[0].data.vertices]
report = {'asset': 'wire-tool', 'dimensions_blender_xyz_m': [max(v[i] for v in verts) - min(v[i] for v in verts) for i in range(3)],
          'bounds_blender': {'min': [min(v[i] for v in verts) for i in range(3)], 'max': [max(v[i] for v in verts) for i in range(3)]},
          'texture_size': n, 'material_count': 1, 'emission': True,
          'lod_triangles': {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects[:3]},
          'hull_triangles': sum(len(p.vertices) - 2 for p in objects[-1].data.polygons), 'vertex_ao_samples': 8,
          'sockets_godot': {'socket_grip_r': [0, 0, 0], 'socket_wire_tip': [0, TIP, 0]}}
export_glb(ITEMS / 'wire-tool.glb', objects + sockets, {'M_Hollow_WireTool': mp})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'wire-tool-source.blend'))
(OUT / 'geometry.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report), flush=True)
