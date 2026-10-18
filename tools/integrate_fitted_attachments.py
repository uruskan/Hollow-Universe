"""Expose existing door/window kit meshes as inventory models and engine-free attachment data.

No geometry is generated: selected imported GLB primitives, authored LODs and convex hulls are
copied byte-for-byte. Outputs are staged by default; --install writes only the seven new item
models, their import remaps and the glass material. The root coordinator applies catalogue-additions.json.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / '.artifacts/window-integration'
MODEL_DIR = 'assets/items/fitted'
WINDOWS = [
    ('metal-window-bars', 'Metal window bars', ['Window_MetalBars'], 'metal', 500, 100, 1),
    ('armoured-window-bars', 'Armoured window bars', ['Window_ArmoredBars'], 'armoured', 1000, 10, 3),
    ('reinforced-glass-window', 'Reinforced glass window', ['Window_GlassFrame', 'Window_GlassPane', 'Window_GlassWire'], 'metal', 500, 100, 2),
    ('horizontal-embrasure', 'Horizontal embrasure', ['Embrasure_Horizontal'], 'metal', 500, 100, 1),
    ('vertical-embrasure', 'Vertical embrasure', ['Embrasure_Vertical'], 'metal', 500, 100, 1),
]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_glb(path):
    raw = path.read_bytes()
    size = struct.unpack_from('<I', raw, 12)[0]
    doc = json.loads(raw[20:20 + size])
    return doc, raw[28 + size:]


def positions(doc, binary, mesh):
    accessor = doc['accessors'][doc['meshes'][mesh]['primitives'][0]['attributes']['POSITION']]
    view = doc['bufferViews'][accessor['bufferView']]
    assert accessor['componentType'] == 5126 and accessor['type'] == 'VEC3'
    start = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    return sorted(set(tuple(round(v, 6) for v in struct.unpack_from('<3f', binary, start + i * view.get('byteStride', 12)))
                      for i in range(accessor['count'])))


def subset(path, parts, offsets, output):
    doc, binary = read_glb(path)
    nodes = []
    for source in doc['nodes']:
        name = source['name']
        part = next((p for p in parts if name == p or name.startswith(p + '_')), None)
        if part is None:
            continue
        assert not any(k in source for k in ('children', 'matrix', 'rotation', 'scale', 'translation'))
        node = copy.deepcopy(source)
        node['translation'] = offsets.get(part, [0, 0, 0])
        node['name'] = name.replace('_LOD', '_lod') + ('_lod0' if name == part else '')
        nodes.append(node)
    mesh_ids = sorted({n['mesh'] for n in nodes})
    meshes = [copy.deepcopy(doc['meshes'][i]) for i in mesh_ids]
    accessor_ids = sorted({a for m in meshes for p in m['primitives'] for a in [*p['attributes'].values(), p['indices']]})
    accessors = [copy.deepcopy(doc['accessors'][i]) for i in accessor_ids]
    view_ids = sorted({a['bufferView'] for a in accessors})
    views, payload = [], bytearray()
    for i in view_ids:
        view = copy.deepcopy(doc['bufferViews'][i])
        start, count = view.get('byteOffset', 0), view['byteLength']
        payload.extend(b'\0' * (-len(payload) % 4))
        view['byteOffset'] = len(payload)
        payload.extend(binary[start:start + count])
        views.append(view)
    for node in nodes:
        node['mesh'] = mesh_ids.index(node['mesh'])
    for mesh in meshes:
        for prim in mesh['primitives']:
            prim['attributes'] = {k: accessor_ids.index(v) for k, v in prim['attributes'].items()}
            prim['indices'] = accessor_ids.index(prim['indices'])
    for accessor in accessors:
        accessor['bufferView'] = view_ids.index(accessor['bufferView'])
    result = {'asset': {'version': '2.0', 'generator': 'Hollow existing-kit subset; original vertex bytes'},
              'scene': 0, 'scenes': [{'nodes': list(range(len(nodes)))}], 'nodes': nodes,
              'meshes': meshes, 'accessors': accessors, 'bufferViews': views,
              'buffers': [{'byteLength': len(payload)}], 'materials': doc['materials']}
    text = json.dumps(result, separators=(',', ':')).encode()
    text += b' ' * (-len(text) % 4)
    payload.extend(b'\0' * (-len(payload) % 4))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(struct.pack('<III', 0x46546C67, 2, 28 + len(text) + len(payload))
                       + struct.pack('<II', len(text), 0x4E4F534A) + text
                       + struct.pack('<II', len(payload), 0x004E4942) + payload)
    mappings = []
    for mat in doc['materials']:
        material = mat['name']
        target = 'fitted_glass' if material == 'Door_glass' else material.lower()
        mappings.append(f'"{material}": {{\n"use_external/enabled": true,\n"use_external/path": "res://assets/materials/{target}.tres"\n}}')
    output.with_suffix('.glb.import').write_text('[remap]\nimporter="scene"\ntype="PackedScene"\n\n[params]\n'
        'nodes/use_name_suffixes=true\nmeshes/generate_lods=false\nmeshes/light_baking=0\nanimation/import=false\n'
        '_subresources={\n"materials": {\n' + ',\n'.join(mappings) + '\n}\n}\n', encoding='utf-8')
    return doc, binary


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--install', action='store_true')
    args = parser.parse_args()
    output_root = ROOT / 'game' if args.install else STAGE / 'game'
    STAGE.mkdir(parents=True, exist_ok=True)
    existing_items = json.loads((ROOT / 'game/data/ground/items.json').read_text(encoding='utf-8-sig'))['items']
    item_ids = {item['key']: item['id'] for item in existing_items}
    additions = {'note': 'Append stable catalogue rows; initial new recipe/health values are provisional balance. Existing double-door definitions keep their indices.',
                 'doors': [], 'items': [], 'recipes': [], 'placement': [], 'models': [], 'techTrees': [], 'sources': []}
    source = ROOT / 'game/assets/building/window_inserts.glb'
    for i, (key, title, parts, protection, health, price, tier) in enumerate(WINDOWS):
        doc, binary = subset(source, parts, {}, output_root / MODEL_DIR / (key + '.glb'))
        hulls = [{'points': positions(doc, binary, n['mesh'])} for n in doc['nodes']
                 if any(n['name'].startswith(p + '_col') for p in parts)]
        assert hulls and all(len(h['points']) == 8 for h in hulls)
        lift = .9 if parts[0].startswith('Embrasure') else .904
        material = 'refined-metal' if protection == 'armoured' else 'metal-fragments'
        additions['doors'].append({'key': key, 'name': title, 'opening': 'window', 'health': health,
            'protection': protection, 'opens': False, 'lockable': False,
            'repair': {'material': material, 'full': max(1, price // 2)},
            'leaves': [{'mesh': parts[0], 'additionalMeshes': parts[1:], 'hinge': [0, lift, 0],
                        'size': [1.8, 1.2, .14], 'openYaw': 0, 'collision': hulls}]})
        additions['items'].append({'id': item_ids.get(key, 236 if i == 4 else 217 + i), 'key': key, 'name': title, 'category': 'construction',
            'description': 'Fits a window opening. Repair with a hammer; pick up with building permission.', 'building': 'door', 'door': key})
        additions['recipes'].append({'output': key, 'seconds': 20, 'workbench': tier, 'ingredients': [[material, price]]})
        additions['techTrees'].append({'workbench': tier, 'node': {'item': key, 'cost': 75 if tier == 1 else 125,
            'after': ['metal-door' if tier == 1 else 'garage-door' if tier == 2 else 'armoured-door']}})
    building = json.loads((ROOT / 'game/data/ground/building.json').read_text(encoding='utf-8-sig'))
    for i, (key, grade, price, tier) in enumerate([('wooden-double-door', 'wood', 600, 0), ('armoured-double-door', 'armoured', 40, 3)]):
        definition = next(d for d in building['doors'] if d['key'] == key)
        parts = [definition['frameMesh']] + [l['mesh'] for l in definition['leaves']]
        offsets = {l['mesh']: l['hinge'] for l in definition['leaves']}
        path = ROOT / f'game/assets/building/doors_{"armored" if grade == "armoured" else grade}.glb'
        subset(path, parts, offsets, output_root / MODEL_DIR / (key + '.glb'))
        additions['items'].append({'id': item_ids.get(key, 222 + i), 'key': key, 'name': definition['name'], 'category': 'construction',
            'description': 'Fits a wall frame.', 'building': 'door', 'door': key})
        ingredients = [['wood', price]] if tier == 0 else [['refined-metal', price], ['gears', 10]]
        recipe = {'output': key, 'seconds': 30, 'ingredients': ingredients}
        if tier:
            recipe['workbench'] = tier
            additions['techTrees'].append({'workbench': tier, 'node': {'item': key, 'cost': 300, 'after': ['armoured-door']}})
        additions['recipes'].append(recipe)
    for item in additions['items']:
        key = item['key']
        additions['placement'].append({'kind': 'door', 'key': key})
        additions['models'].append({'key': key, 'kind': 'item', 'class': 'door', 'file': f'res://{MODEL_DIR}/{key}.glb',
            'lods': 3, 'collision': 'convex', 'icon': {'view': [24, 12, 0]},
            'source': 'PSG Door Window Kit R01: existing imported meshes; tools/integrate_fitted_attachments.py'})
    for path in [source, ROOT / 'game/assets/building/doors_wood.glb', ROOT / 'game/assets/building/doors_armored.glb']:
        additions['sources'].append({'path': path.relative_to(ROOT).as_posix(), 'sha256': digest(path)})
    glass = output_root / 'assets/materials/fitted_glass.tres'
    glass.parent.mkdir(parents=True, exist_ok=True)
    glass.write_text('[gd_resource type="StandardMaterial3D" format=3]\n\n[resource]\nresource_name = "Door_glass"\n'
        'transparency = 1\nalbedo_color = Color(0.62, 0.70, 0.72, 0.28)\nroughness = 0.12\nmetallic = 0.1\n', encoding='utf-8')
    (STAGE / 'catalogue-additions.json').write_text(json.dumps(additions, indent=2) + '\n', encoding='utf-8')
    print(f'Prepared {len(additions["items"])} models; five window definitions; catalogue: {STAGE / "catalogue-additions.json"}')


if __name__ == '__main__':
    main()
