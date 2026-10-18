"""Original wooden mending spool, wound thread and steel needle.

Run: blender -b --factory-startup -t 2 --python art/models/sewing_kit.py
Only this item's assets are written. No simulation, baking or external pixels.
The Rust public item image was inspected for readability, never imported.
"""
from pathlib import Path
import json
import math
import sys
import bpy
import bmesh
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from hollow import ITEMS, TEXTURES, REPO
from hollow.export import collection, export_glb, convex_hull
from hollow.godot import write_material
from hollow.textures import write_png, write_texture_import, normal_from_height

OUT = REPO / '.artifacts/sewing-kit-r01'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('SewingKit')

# One original 512px atlas, with padded horizontal material bands. Mesh UVs
# stay inside each band so the metal mask cannot leak into the wood or thread.
n = 512
y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
albedo = np.ones((n, n, 4), np.float32)
orm = np.ones_like(albedo)
height = np.zeros((n, n), np.float32)
orm[:, :, 2] = 0
for band in range(4):
    region = (y >= band / 4) & (y < (band + 1) / 4)
    v = (y - band / 4) * 4
    grain = .025 * np.sin(x * 91 + np.sin(v * 14)) + .018 * np.sin(x * 211 + v * 7)
    wear = .045 * np.sin(x * 17 + v * 9) * np.cos(v * 23 - x * 3)
    if band == 0:  # Sage thread, horizontal winding and fine twisted fibres.
        h = .10 * np.cos(v * math.tau * 42 + .5 * np.sin(x * 19))
        h += .018 * np.sin(x * 743 + v * 271)
        color = np.array([.43, .49, .29])
        shade = .90 + h * .38 + wear
        rough, metal = .9, 0
    elif band == 1:  # Turned, worn hardwood.
        h = grain + .012 * np.sin(x * 419 + v * 12)
        color = np.array([.62, .43, .24])
        shade = .88 + grain * 2 + wear
        rough, metal = .72, 0
    elif band == 2:  # Polished grey needle, understated lengthwise scratches.
        h = .008 * np.sin(x * 387)
        color = np.array([.57, .60, .62])
        shade = .98 + grain * .35
        rough, metal = .29, .95
    else:  # Pale trailing thread.
        h = .035 * np.sin(v * 603 + x * 191)
        color = np.array([.63, .66, .43])
        shade = .95 + wear
        rough, metal = .87, 0
    albedo[region, :3] = (shade[:, :, None] * color)[region]
    height[region] = h[region]
    orm[region, 1] = rough
    orm[region, 2] = metal
paths = {}
for key, pixels in [('albedo', albedo), ('orm', orm), ('normal', normal_from_height(height, .8))]:
    paths[key] = write_png(TEXTURES / 'sewing-kit' / f'sewing_kit_{key}.png', pixels)
    write_texture_import(paths[key], normal_map=key == 'normal')
material_path = write_material('sewing-kit', 'M_Hollow_SewingKit', albedo=paths['albedo'],
    orm=paths['orm'], normal=paths['normal'], normal_scale=.55, vertex_colour=True, vertex_colour_srgb=False)
mat = bpy.data.materials.new('M_Hollow_SewingKit')
mat.use_nodes = True
shader = mat.node_tree.nodes.get('Principled BSDF')
for key in ('albedo', 'orm', 'normal'):
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(str(paths[key]))
    if key != 'albedo': tex.image.colorspace_settings.name = 'Non-Color'
    if key == 'albedo': mat.node_tree.links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    elif key == 'normal':
        node = mat.node_tree.nodes.new('ShaderNodeNormalMap'); node.inputs['Strength'].default_value = .55
        mat.node_tree.links.new(tex.outputs['Color'], node.inputs['Color'])
        mat.node_tree.links.new(node.outputs['Normal'], shader.inputs['Normal'])
    else:
        node = mat.node_tree.nodes.new('ShaderNodeSeparateColor')
        mat.node_tree.links.new(tex.outputs['Color'], node.inputs[0])
        mat.node_tree.links.new(node.outputs[1], shader.inputs['Roughness'])
        mat.node_tree.links.new(node.outputs[2], shader.inputs['Metallic'])


class Mesh:
    def __init__(self): self.vertices, self.faces, self.uvs, self.colors = [], [], [], []

    def vertex(self, p, u, v, band, tint=1):
        self.vertices.append(tuple(p))
        # PNG rows are top-down; Blender UV V is bottom-up. The exporter flips
        # it back for glTF, preserving the intended atlas material band.
        self.uvs.append((.015 + .97 * u, 1 - (band / 4 + .012 + .226 * v)))
        self.colors.append((tint, tint, tint, 1))

    def grid(self, start, rows, columns):
        for j in range(rows - 1):
            for i in range(columns - 1):
                a = start + j * columns + i
                self.faces.append((a, a + 1, a + columns + 1, a + columns))

    def lathe(self, profile, segments, band, ripple=False):
        start = len(self.vertices)
        for j, (r, z) in enumerate(profile):
            for i in range(segments + 1):
                a = i / segments * math.tau
                r1 = r + (.0005 * math.sin(a * 3 + z * 45) if ripple else 0)
                self.vertex((r1 * math.cos(a), r1 * math.sin(a), z), i / segments,
                    j / (len(profile) - 1), band, .94 + .06 * math.cos(a * 2 + z * 9))
        self.grid(start, len(profile), segments + 1)

    def tube(self, points, radius, sides, band, closed=False, taper=False):
        points = [Vector(p) for p in points]
        start = len(self.vertices)
        for j, p in enumerate(points):
            prev = points[j - 1] if j else points[-2] if closed else p
            nxt = points[j + 1] if j + 1 < len(points) else points[1] if closed else p
            axis = (nxt - prev).normalized()
            tangent = axis.cross(Vector((0, -1, .02))).normalized()
            side = axis.cross(tangent).normalized()
            r = radius * (1 - .96 * (j / (len(points) - 1)) ** 7 if taper else 1)
            for i in range(sides + 1):
                a = i / sides * math.tau
                self.vertex(p + r * (math.cos(a) * tangent + math.sin(a) * side),
                    i / sides, j / (len(points) - 1), band)
        self.grid(start, len(points), sides + 1)
        if not closed:
            self.faces.append(tuple(start + i for i in reversed(range(sides))))
            end = start + (len(points) - 1) * (sides + 1)
            self.faces.append(tuple(end + i for i in range(sides)))

    def object(self, name):
        mesh = bpy.data.meshes.new(name); mesh.from_pydata(self.vertices, [], self.faces); mesh.update()
        obj = bpy.data.objects.new(name, mesh); col.objects.link(obj); mesh.materials.append(mat)
        uv = mesh.uv_layers.new(name='UVMap')
        for p in mesh.polygons:
            p.use_smooth = True
            for li in p.loop_indices: uv.data[li].uv = self.uvs[mesh.loops[li].vertex_index]
        colors = mesh.color_attributes.new(name='Col', type='FLOAT_COLOR', domain='POINT')
        for i, color in enumerate(self.colors): colors.data[i].color = color
        mesh.color_attributes.active_color = colors
        bm = bmesh.new(); bm.from_mesh(mesh)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.000001)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(mesh); bm.free()
        return obj


objects = []
for lod, segments, wraps, sides in [(0, 40, 5, 6), (1, 24, 3, 5), (2, 12, 1, 4)]:
    m = Mesh()
    # Closed hollow wooden bobbin; bevels are explicit and survive every LOD.
    wood = [(.009, .004), (.009, .137), (.011, .141), (.037, .141), (.041, .137),
        (.041, .131), (.037, .128), (.019, .128), (.019, .016), (.040, .016),
        (.043, .012), (.043, .005), (.039, .001), (.012, .001), (.009, .004)]
    m.lathe(wood, segments, 1)
    count = [30, 16, 6][lod]
    thread = [(.019, .018), (.032, .018)]
    thread += [(.0345 + .001 * math.sin(j * 2.1), .02 + .105 * j / count) for j in range(count + 1)]
    thread += [(.030, .128), (.019, .128), (.019, .018)]
    m.lathe(thread, segments, 0, ripple=True)
    # A handful of diagonal windings communicates loose hand winding at slot size.
    for w in range(wraps):
        points = []
        for i in range(segments + 1):
            a = i / segments * math.tau
            z = .040 + .014 * w + .019 * math.sin(a + w * .8)
            r = .0365 + .0005 * math.sin(a * 3 + z * 45)
            points.append((r * math.cos(a), r * math.sin(a), z))
        m.tube(points, .00075, sides, 0, closed=True)
    # Large mending needle. Its eye is a real hole, not painted onto a solid rod.
    eye = Vector((.039, -.044, .131))
    axis = Vector((-.62, -.02, -.78)).normalized()
    side = Vector((axis.z, 0, -axis.x)).normalized()
    eye_steps = [24, 16, 10][lod]
    points = [eye + .010 * math.cos(i / eye_steps * math.tau) * axis +
        .0035 * math.sin(i / eye_steps * math.tau) * side for i in range(eye_steps + 1)]
    m.tube(points, .00125, sides, 2, closed=True)
    m.tube([eye + axis * (.009 + .128 * i / 8) for i in range(9)], .0016, sides, 2, taper=True)
    # Thread through the needle's eye, hanging beside the spool and returning to it.
    control = [eye, Vector((.074, -.038, .073)), Vector((.061, -.052, .015)), Vector((.027, -.030, .038))]
    steps = [40, 24, 12][lod]
    points = []
    for i in range(steps + 1):
        t = i / steps
        points.append((1-t)**3*control[0]+3*(1-t)**2*t*control[1]+3*(1-t)*t*t*control[2]+t**3*control[3])
    m.tube(points, .00105, sides, 3)
    objects.append(m.object(f'SewingKit_lod{lod}'))

# All LODs use LOD0's bottom-centre origin, so switching detail never jumps.
lo = Vector(tuple(min(v.co[i] for v in objects[0].data.vertices) for i in range(3)))
hi = Vector(tuple(max(v.co[i] for v in objects[0].data.vertices) for i in range(3)))
offset = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
for obj in objects:
    for v in obj.data.vertices: v.co -= offset
objects.append(convex_hull([objects[0]], 'SewingKit_hull0-convcolonly', col, max_faces=48))
report = {'asset': 'sewing-kit', 'dimensions_blender_xyz_m': list(hi-lo), 'texture_size': n,
    'material_count': 1, 'lod_triangles': {o.name: sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects[:3]},
    'hull_triangles': sum(len(p.vertices)-2 for p in objects[-1].data.polygons)}
for o, budget in zip(objects[:3], [10000, 5500, 2200]):
    assert report['lod_triangles'][o.name] <= budget, report
export_glb(ITEMS / 'sewing-kit.glb', objects, {'M_Hollow_SewingKit': material_path})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'sewing-kit-source.blend'))
(OUT / 'geometry.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report), flush=True)
