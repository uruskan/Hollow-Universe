"""Compile original Hollow monument metadata into shared engine-free placement/collision data.

Run with Blender --background --factory-startup --python tools/blender/environment_monuments.py.
No reference-game geometry is used. Rendering keeps the existing original GLBs and kit pieces.
"""
import bpy, bmesh, json, math, sys
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'game/data/monuments'
KEYS = ['weather-station', 'crashed-skiff', 'hab-pods', 'wellhead', 'relay-mast']

def quaternion(value):
    return Quaternion(value or (1, 0, 0, 0)).normalized()

def geometry(shape):
    bm = bmesh.new()
    if 'hull' in shape:
        for point in shape['hull']:
            bm.verts.new(point)
        bmesh.ops.convex_hull(bm, input=list(bm.verts), use_existing_faces=False)
    elif 'box' in shape:
        half = shape['half']
        bmesh.ops.create_cube(bm, size=2)
        for vertex in bm.verts:
            vertex.co = Vector(tuple(vertex.co[i] * half[i] for i in range(3)))
    elif 'cyl' in shape:
        # Metadata is Godot Y-up already; a cylinder's local long axis is Y.
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=True, segments=12,
                              radius1=shape['radius'], radius2=shape['radius'], depth=shape['half'] * 2)
        for vertex in bm.verts:
            vertex.co = Vector((vertex.co.x, vertex.co.z, -vertex.co.y))
    else:
        raise ValueError('Unknown authored collision shape: ' + str(shape.keys()))
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.verts.ensure_lookup_table()
    bm.verts.index_update()
    rotation = quaternion(shape.get('rot'))
    centre = Vector(shape.get('box', shape.get('cyl', (0, 0, 0))))
    vertices = [rotation @ vertex.co + centre for vertex in bm.verts]
    triangles = [vertex.index for face in bm.faces for vertex in face.verts]
    bm.free()
    return vertices, triangles

rows = []
for key in KEYS:
    row = json.loads((SOURCE / (key + '.json')).read_text())
    positions, indices = [], []
    def append_shapes(shapes, rotation=Quaternion((1, 0, 0, 0)), at=Vector((0, 0, 0))):
        for shape in shapes:
            vertices, triangles = geometry(shape)
            offset = len(positions)
            positions.extend([[round(v, 6) for v in rotation @ point + at] for point in vertices])
            indices.extend(index + offset for index in triangles)
    append_shapes(row['collision'])
    for piece in row['pieces']:
        data = json.loads((SOURCE / 'kit' / (piece['piece'] + '.json')).read_text())
        append_shapes(data.get('collision', []), quaternion(piece.get('rot')), Vector(piece['at']))
    output = {name: row[name] for name in ['key', 'title', 'model', 'bounds', 'pieces', 'markers', 'pad']}
    output['positions'], output['indices'] = positions, indices
    rows.append(output)
    print(f'{key}: {len(indices) // 3} collision triangles')
sys.path.insert(0, str(Path(__file__).resolve().parent))
import environment_substation
rows.append(environment_substation.build(ROOT, geometry))
destination = ROOT / 'src/Hollow.Surface/Data/monuments.json'
destination.write_text(json.dumps(rows, separators=(',', ':')), encoding='utf-8')
print(destination)
