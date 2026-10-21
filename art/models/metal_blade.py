"""Original salvaged steel blade, authored after inspecting the public Rust icon.

blender -b --factory-startup -t 2 --python art/models/metal_blade.py
Writes only metal-blade assets. No external meshes, pixels, simulation or bake.
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

OUT = REPO / '.artifacts/metal-blade-r01'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('MetalBlade')

# An angular, tapered machine blade. Small lower-edge losses are geometry, and
# the mounting hole remains open at every LOD. Coordinates are metres in X/Z.
outline = [(-.160, .195), (.145, .174), (.158, .159), (.160, .091),
    (.155, .080), (.143, .078), (.138, .070), (.126, .071),
    (.119, .067), (.105, .061), (.087, .052), (.070, .050),
    (.025, .035), (-.035, .015), (-.108, .001), (-.139, .000),
    (-.146, .006), (-.160, .183)]
centre = np.array([-.092, .133])

# One 1K atlas: the broad steel faces occupy the first 80%; narrow padded strips
# hold freshly ground edges and the darker thickness/hole wall. Rust is sparse.
n = 1024
row, u = np.mgrid[0:n, 0:n].astype(np.float32) / n
v = row / .8
px, pz = u * .36 - .18, (1-v) * .22 - .01
rng = np.random.default_rng(158713)
noise = rng.uniform(-1, 1, (n, n)).astype(np.float32)
cloud = np.zeros((n, n), np.float32)
for freq, weight in [(5, .46), (13, .26), (33, .16), (77, .08)]:
    for direction in range(3):
        a = rng.uniform(0, math.tau)
        cloud += weight / 3 * np.sin((u * math.cos(a) + v * math.sin(a)) * freq + rng.uniform(0, math.tau))
hole_dist = np.hypot(px - centre[0], pz - centre[1])
rust = np.clip((cloud - .12) * 3.5, 0, .62)
rust += np.exp(-((hole_dist-.017)/.006)**2) * (.25 + .18*np.sin(u*93+v*61))
rust = np.clip(rust, 0, .65)
steel = np.array([.54, .555, .565], np.float32)
rust_color = np.array([.29, .18, .115], np.float32)
shade = .96 + cloud * .12 + noise * .075
albedo = np.ones((n, n, 4), np.float32)
albedo[:, :, :3] = steel * shade[:, :, None] * (1-rust[:, :, None]) + rust_color * rust[:, :, None]
height = noise * .035 + cloud * .035 - rust * .07
scratches = np.zeros((n, n), np.float32)
# Scratches of differing lengths/directions; no tiled or copied photo content.
for i in range(110):
    sx, sy = rng.uniform(.04, .96), rng.uniform(.04, .76)
    angle = rng.uniform(.55, 1.15) if i % 4 else rng.uniform(-1.4, 1.4)
    dx, dy = u-sx, row-sy
    along = dx*math.cos(angle)+dy*math.sin(angle)
    across = -dx*math.sin(angle)+dy*math.cos(angle)
    length, width = rng.uniform(.004, .075), rng.uniform(.00035, .00095)
    line = np.exp(-(across/width)**2) * np.clip(1-np.abs(along)/length, 0, 1)
    scratches = np.maximum(scratches, line * rng.uniform(.18, .8))
albedo[:, :, :3] += scratches[:, :, None] * .18
height -= scratches * .12
# Broken silver abrasions near the grind, rather than a perfectly clean border.
distance_to_edge = np.full_like(u, 1)
for i in range(4, len(outline)):
    ax, az = outline[i]; bx, bz = outline[(i+1) % len(outline)]
    dx, dz = bx-ax, bz-az
    t = np.clip(((px-ax)*dx+(pz-az)*dz)/(dx*dx+dz*dz), 0, 1)
    distance_to_edge = np.minimum(distance_to_edge, np.hypot(px-ax-t*dx, pz-az-t*dz))
wear_grid = rng.uniform(0, 1, (96, 128))
wear_rows = np.array([np.interp(np.linspace(0, 127, n), np.arange(128), r) for r in wear_grid])
wear = np.array([np.interp(np.linspace(0, 95, n), np.arange(96), c) for c in wear_rows.T]).T
abrasion = np.exp(-((distance_to_edge-.010)/.007)**2) * np.clip((wear+noise*.25-.48)*3, 0, 1)
albedo[:, :, :3] += abrasion[:, :, None]*.19
height -= abrasion*.020
orm = np.ones_like(albedo)
orm[:, :, 1] = np.clip(.60 + cloud*.18 + rust*.30 - scratches*.16, .38, .92)
orm[:, :, 2] = .92 * (1-rust)
for start, end, color, roughness in [(.81, .90, [.76, .78, .80], .43), (.92, 1, [.27, .28, .29], .67)]:
    mask = (row >= start) & (row < end)
    grinding = .005*np.sin(u*891+row*700)+.008*noise
    albedo[mask, :3] = (np.array(color)*(1+grinding[:, :, None]))[mask]
    orm[mask, 1] = roughness
    orm[mask, 2] = .68 if start == .81 else .94
    height[mask] = (noise*.005)[mask]
paths = {}
for key, pixels in [('albedo', albedo), ('orm', orm), ('normal', normal_from_height(height, 3.5))]:
    paths[key] = write_png(TEXTURES/'metal-blade'/f'metal_blade_{key}.png', pixels)
    write_texture_import(paths[key], normal_map=key == 'normal')
material_path = write_material('metal-blade', 'M_Hollow_MetalBlade', albedo=paths['albedo'],
    orm=paths['orm'], normal=paths['normal'], normal_scale=.65, vertex_colour=False)
mat = bpy.data.materials.new('M_Hollow_MetalBlade')
mat.use_nodes = True
shader = mat.node_tree.nodes.get('Principled BSDF')
for key in ('albedo', 'orm', 'normal'):
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(str(paths[key]))
    if key != 'albedo': tex.image.colorspace_settings.name = 'Non-Color'
    if key == 'albedo': mat.node_tree.links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    elif key == 'normal':
        node = mat.node_tree.nodes.new('ShaderNodeNormalMap'); node.inputs['Strength'].default_value = .65
        mat.node_tree.links.new(tex.outputs['Color'], node.inputs['Color'])
        mat.node_tree.links.new(node.outputs['Normal'], shader.inputs['Normal'])
    else:
        node = mat.node_tree.nodes.new('ShaderNodeSeparateColor')
        mat.node_tree.links.new(tex.outputs['Color'], node.inputs[0])
        mat.node_tree.links.new(node.outputs[1], shader.inputs['Roughness'])
        mat.node_tree.links.new(node.outputs[2], shader.inputs['Metallic'])


def cross(a, b): return a[0]*b[1]-a[1]*b[0]


def boundary(angle):
    direction = np.array([math.cos(angle), math.sin(angle)])
    hits = []
    for i, point in enumerate(outline):
        a = np.array(point); b = np.array(outline[(i+1) % len(outline)])
        edge = b-a; denominator = cross(direction, edge)
        if abs(denominator) < 1e-10: continue
        distance = cross(a-centre, edge)/denominator
        fraction = cross(a-centre, direction)/denominator
        if distance > 0 and -.00001 <= fraction <= 1.00001:
            hits.append((distance, i))
    distance, edge = min(hits)
    # Broad grind on the cutting edges; thin chamfer on the top and narrow end.
    width = .010 if edge >= 4 else .0012
    return centre+direction*distance, centre+direction*(distance-width)


objects = []
for lod, steps in [(0, 80), (1, 44), (2, 24)]:
    # Include every corner angle so LODs retain identical nicks and silhouette.
    angles = sorted(set([round(math.atan2(z-centre[1], x-centre[0]), 7) for x,z in outline]
        + [round(-math.pi+i*math.tau/steps, 7) for i in range(steps)]))
    vertices, faces, face_types = [], [], []
    rings = []
    for side in (-1, 1):
        for kind in range(4):
            ring = []
            for angle in angles:
                direction = np.array([math.cos(angle), math.sin(angle)])
                outer, inner = boundary(angle)
                position = centre+direction*[.011, .0125][kind] if kind < 2 else (inner if kind == 2 else outer)
                depth = [.0019, .0026, .0026, .00045][kind]
                ring.append(len(vertices))
                vertices.append((float(position[0]), side*depth, float(position[1])*1.18))
            rings.append(ring)
    def bridge(a, b, material):
        for j in range(len(angles)):
            nxt = (j+1) % len(angles)
            faces.append((a[j], a[nxt], b[nxt], b[j])); face_types.append(material)
    for side in range(2):
        for k, kind in enumerate(['edge', 'face', 'edge']): bridge(rings[side*4+k], rings[side*4+k+1], kind)
    bridge(rings[0], rings[4], 'wall')
    bridge(rings[3], rings[7], 'wall')
    mesh = bpy.data.meshes.new(f'MetalBlade_lod{lod}')
    mesh.from_pydata(vertices, [], faces); mesh.update()
    uv = mesh.uv_layers.new(name='UVMap')
    for polygon, kind in zip(mesh.polygons, face_types):
        for loop in polygon.loop_indices:
            x,y,z = vertices[mesh.loops[loop].vertex_index]
            uu = (x+.18)/.36
            vv = (1-(z/1.18+.01)/.22)*.8 if kind == 'face' else (.85 if kind == 'edge' else .96) + y*2
            uv.data[loop].uv = (uu, 1-vv)
        # Keep the planar faces and grinding bevels flat. Smooth the bore wall.
        polygon.use_smooth = kind == 'wall' and polygon.index < len(faces)-len(angles)
    bm = bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(mesh.name, mesh); col.objects.link(obj)
    mesh.materials.append(mat); objects.append(obj)

objects.append(convex_hull([objects[0]], 'MetalBlade_hull0-convcolonly', col, max_faces=32))
report = {'asset':'metal-blade', 'dimensions_blender_xyz_m':[.320,.0052,.2301],
    'texture_size':n, 'material_count':1,
    'lod_triangles':{o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects[:3]},
    'hull_triangles':sum(len(p.vertices)-2 for p in objects[-1].data.polygons)}
export_glb(ITEMS/'metal-blade.glb', objects, {'M_Hollow_MetalBlade':material_path})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'metal-blade-source.blend'))
(OUT/'geometry.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report),flush=True)
