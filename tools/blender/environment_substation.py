"""Vendoring and shared collision for one deliberate R01 industrial landmark.

Called by environment_monuments.py in background Blender. Native scenes keep their authored PBR and LODs;
their actual CollisionShape3D hulls are transformed into the same shared physics mesh as other monuments.
"""
import hashlib, json, re, shutil, math
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix, Euler

ASSETS = Path('D:/GameDev/Assets')
PARTS = [
    ('Hollow_Substation_R01', 'service-court', (0, 0, 0), 0),
    ('Hollow_Substation_R01', 'oil-transformer', (0, .28, -1.5), 0),
    ('Hollow_Substation_R01', 'switchgear-cabinet', (-3.6, .07, -.6), 0),
    ('Hollow_Substation_R01', 'cable-reel', (-3.5, .07, 2.8), -.25),
    ('Hollow_Substation_R01', 'jersey-barrier', (2.1, .07, 3.9), -.12),
    # A compact isolated switch bay beside the transformer court, leaving the front walking route open.
    ('Hollow_PowerKit_R01', 'breaker', (9, 0, -1), 0),
    ('Hollow_PowerKit_R01', 'disconnect-open', (9, 0, 2.5), 0),
    ('Hollow_PowerKit_R01', 'termination-pedestal', (7, 0, -3.7), 0),
    # One dismantling spot beside the transformer and a seep under its front oil valve.
    ('Hollow_RuinDressing_R01', 'hardware-cluster', (-2.2, .07, -.6), -.3),
    ('Hollow_RuinDressing_R01', 'decal-oil-seep', (.9, .29, -.5), .2),
]

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def values(line):
    return [float(x) for x in re.search(r'\((.*)\)', line).group(1).split(',')]

def native_hulls(path):
    """Read only declared convex shapes and node transforms; never evaluate scene scripts or mesh strings."""
    shapes, nodes, block = {}, [], None
    for line in path.read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('['):
            attrs = dict(re.findall(r'(\w+)="([^"]*)"', line))
            block = None
            if line.startswith('[sub_resource ') and attrs.get('type') == 'ConvexPolygonShape3D':
                block = ('shape', attrs['id'])
            elif line.startswith('[node '):
                node = dict(attrs, matrix=Matrix.Identity(4))
                nodes.append(node)
                block = ('node', node)
        elif block and block[0] == 'shape' and line.startswith('points = '):
            p = values(line)
            shapes[block[1]] = [Vector(p[i:i+3]) for i in range(0, len(p), 3)]
        elif block and block[0] == 'node':
            node = block[1]
            if line.startswith('transform = '):
                a = values(line)
                node['matrix'] = Matrix(((a[0],a[3],a[6],a[9]),(a[1],a[4],a[7],a[10]),
                                         (a[2],a[5],a[8],a[11]),(0,0,0,1)))
            elif line.startswith('position = '): node['position'] = Vector(values(line))
            elif line.startswith('rotation = '): node['rotation'] = Euler(values(line), 'YXZ')
            elif line.startswith('scale = '): node['scale'] = Vector(values(line))
            elif line.startswith('shape = SubResource('): node['shape'] = re.search(r'"([^"]*)"', line).group(1)
    global_matrices, hulls = {}, []
    for node in nodes:
        parent = node.get('parent')
        key = '.' if parent is None else node['name'] if parent == '.' else parent + '/' + node['name']
        matrix = node['matrix']
        if any(p in node for p in ('position','rotation','scale')):
            matrix = Matrix.LocRotScale(node.get('position',Vector()), node.get('rotation',Euler()).to_quaternion(),
                                       node.get('scale',Vector((1,1,1))))
        if parent is not None: matrix = global_matrices[parent] @ matrix
        global_matrices[key] = matrix
        if node.get('type') == 'CollisionShape3D' and 'shape' in node:
            if node['shape'] not in shapes:
                raise ValueError(f'Unsupported/non-convex authored collision in {path}: {node["shape"]}')
            hulls.append([matrix @ v for v in shapes[node['shape']]])
    return hulls

def build(root, geometry):
    receipts, copied = {}, set()
    def copy(package, relative):
        key = (package, relative)
        if key in copied: return
        copied.add(key)
        source = ASSETS / package
        if package not in receipts:
            manifest = source / 'Reports/package_manifest.json'
            roster = json.loads(manifest.read_text(encoding='utf-8-sig'))['files']
            receipts[package] = {'source':str(source), 'source_manifest_sha256':digest(manifest),
                                 'roster': {p['path']: p['sha256'] for p in roster}, 'files':[]}
        receipt = receipts[package]
        src = source / relative
        source_hash = digest(src)
        if receipt['roster'].get(relative) != source_hash:
            raise ValueError(f'Source manifest mismatch: {src}')
        dest = root / 'game/assets/revised' / package / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        if dest.suffix in ('.tscn','.tres'):
            text = src.read_text(encoding='utf-8-sig')
            for dependency in re.findall(r'path="res://([^"]+)"', text): copy(package,dependency)
            dest.write_text(text.replace('res://',f'res://assets/revised/{package}/'),encoding='utf-8')
        else: shutil.copyfile(src,dest)
        receipt['files'].append({'source':relative,'destination':dest.relative_to(root).as_posix(),
                                 'source_sha256':source_hash,'installed_sha256':digest(dest)})
    positions, indices, scene_lines = [], [], ['[gd_scene format=3]', '']
    for i, (package,key,at,yaw) in enumerate(PARTS):
        relative = f'Scenes/{key}.tscn'
        copy(package,relative)
        scene_lines.append(f'[ext_resource type="PackedScene" path="res://assets/revised/{package}/{relative}" id="{i+1}"]')
        transform = Matrix.Translation(Vector(at)) @ Matrix.Rotation(yaw,4,'Y')
        for hull in native_hulls(ASSETS / package / relative):
            points, triangles = geometry({'hull':[tuple(transform @ v) for v in hull]})
            offset = len(positions)
            positions.extend([[round(v,6) for v in p] for p in points])
            indices.extend(offset + index for index in triangles)
    scene_lines += ['', '[node name="ServiceSubstation" type="Node3D"]']
    for i, (_,key,at,yaw) in enumerate(PARTS):
        scene_lines += ['', f'[node name="{key}" parent="." instance=ExtResource("{i+1}")]',
                        f'position = Vector3({at[0]}, {at[1]}, {at[2]})', f'rotation = Vector3(0, {yaw}, 0)']
    target = root / 'game/assets/revised/service-substation.tscn'
    target.write_text('\n'.join(scene_lines)+'\n',encoding='utf-8')
    for receipt in receipts.values(): del receipt['roster']
    report = {'method':'Selective, manifest-verified native Godot dependency integration; authored convex hulls compiled for shared physics',
              'packages':receipts, 'assembly_sha256':digest(target), 'collision_triangles':len(indices)//3,
              'layout':[{'package':p,'part':k,'at':at,'yaw_radians':y} for p,k,at,y in PARTS]}
    (root/'game/assets/revised/substation-integration.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(f'R01 substation: {len(copied)} verified dependencies, {len(indices)//3} shared collision triangles')
    return {'key':'service-substation','title':'Service Substation','model':'res://assets/revised/service-substation.tscn',
            'bounds':{'min':[-6.5,-.31,-5.5],'max':[11,4.55,5.5]}, 'pieces':[], 'pad':{'ground':'gravel'},
            'markers':[{'kind':'loot','at':[-4.8,.07,1.4],'yaw':0,'table':'tech-scrap'},
                       {'kind':'loot','at':[4.4,.07,2.3],'yaw':0,'table':'crate'},
                       {'kind':'loot','at':[10.6,.05,-3.5],'yaw':0,'table':'barrel'}],
            'positions':positions,'indices':indices}
