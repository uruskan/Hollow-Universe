"""Prepare/validate owned PSG node textures; --install admits the reviewed local conversion.

Run after tools/blender/resource_nodes.py. Source bytes are verified against the owner's release roster.
Default writes only .artifacts/resource-nodes. --install replaces twelve existing GLBs and their material
imports, updates only four catalogue entries, and regenerates measured weak-point surfaces.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import sys
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path('D:/GameDev/Assets/PSG_ResourceNodes_R01')
STAGE = ROOT / '.artifacts/resource-nodes'
PACK = 'assets/revised/PSG_ResourceNodes_R01'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def resized_color(image):
    # Albedo filtering in linear light, then stored as sRGB for the material sampler.
    channels = []
    for channel in image.split():
        values = np.asarray(channel, dtype=np.float32) / 255
        linear = np.where(values <= .04045, values / 12.92, ((values + .055) / 1.055) ** 2.4)
        small = np.asarray(Image.fromarray(linear).resize((2048, 2048), Image.Resampling.LANCZOS))
        small = np.clip(small, 0, 1)
        srgb = np.where(small <= .0031308, small * 12.92, 1.055 * small ** (1 / 2.4) - .055)
        channels.append(np.rint(srgb * 255).astype(np.uint8))
    return Image.fromarray(np.stack(channels, axis=-1))


def prepare(receipt):
    roster = json.loads((SOURCE / 'Reports/release_checksums.json').read_text())
    outputs = []
    for asset in receipt['assets']:
        stem = f"{asset['key']}-{asset['stage']}"
        paths = {}
        asset['texture_conversion'] = []
        for channel, source_name in asset['textures'].items():
            relative = source_name.replace('\\', '/')
            source = SOURCE / relative
            assert digest(source) == roster[relative]['sha256'], f'Source changed: {source}'
            name = channel.replace('N_DX', 'NormalGL')
            target = STAGE / 'Textures' / f'{stem}-{name}.png'
            target.parent.mkdir(exist_ok=True)
            image = Image.open(source).convert('RGB')
            assert image.size == (4096, 4096), source
            if channel == 'BC':
                image = resized_color(image)
            else:
                image = image.resize((2048, 2048), Image.Resampling.LANCZOS)
                if channel == 'N_DX':
                    values = np.asarray(image, dtype=np.float32) / 127.5 - 1
                    values[:, :, 1] *= -1
                    values /= np.maximum(np.linalg.norm(values, axis=2, keepdims=True), .001)
                    image = Image.fromarray(np.rint(np.clip(values * .5 + .5, 0, 1) * 255).astype(np.uint8))
            image.save(target, compress_level=6)
            paths[channel] = f'res://{PACK}/Textures/{target.name}'
            texture_import = '\n'.join(['[remap]', 'importer="texture"', 'type="CompressedTexture2D"', '', '[params]',
                'compress/mode=2', 'compress/high_quality=true', f'compress/normal_map={1 if channel == "N_DX" else 2}',
                'mipmaps/generate=true', 'process/normal_map_invert_y=false', 'detect_3d/compress_to=0', ''])
            target.with_suffix('.png.import').write_text(texture_import, encoding='utf-8')
            asset['texture_conversion'].append({'source': relative, 'source_sha256': digest(source),
                'output': 'Textures/' + target.name, 'output_sha256': digest(target),
                'size': [2048, 2048], 'normal': 'OpenGL; green flipped and vectors renormalized' if channel == 'N_DX' else None})
            outputs.extend([target, target.with_suffix('.png.import')])
        material = STAGE / 'Materials' / f'{stem}.tres'
        material.parent.mkdir(exist_ok=True)
        material.write_text('\n'.join(['[gd_resource type="ORMMaterial3D" load_steps=4 format=3]', '',
            f'[ext_resource type="Texture2D" path="{paths["BC"]}" id="1"]',
            f'[ext_resource type="Texture2D" path="{paths["ORM"]}" id="2"]',
            f'[ext_resource type="Texture2D" path="{paths["N_DX"]}" id="3"]', '', '[resource]',
            f'resource_name = "{asset["material"]}"', 'vertex_color_use_as_albedo = false',
            'albedo_texture = ExtResource("1")', 'orm_texture = ExtResource("2")', 'ao_enabled = true',
            'normal_enabled = true', 'normal_texture = ExtResource("3")', 'texture_filter = 5', '']), encoding='utf-8')
        outputs.append(material)
        print('TEXTURES_READY', stem, flush=True)
    receipt['texture_policy'] = '2K RGB8 derivatives; source4K retained; linear albedo filtering; ORM linear data; OpenGL normals; mipmapped high-quality VRAM compression.'
    receipt['output_roster'] = [{'path': p.relative_to(STAGE).as_posix(), 'sha256': digest(p)} for p in outputs]
    (STAGE / 'integration.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')


def validate(receipt):
    for asset in receipt['assets']:
        path = STAGE / asset['glb']
        assert digest(path) == asset['glb_sha256'], path
        data = path.read_bytes()
        assert data[:4] == b'glTF' and struct.unpack_from('<I', data, 8)[0] == len(data)
        length, kind = struct.unpack_from('<II', data, 12)
        assert kind == 0x4e4f534a
        doc = json.loads(data[20:20 + length])
        names = {n.get('name') for n in doc['nodes']}
        assert all(f"{asset['key']}_lod{i}" in names for i in range(4)), names
        assert f"{asset['key']}_hull0-convcolonly" in names
        assert len(doc['materials']) == 1 and doc['materials'][0]['name'] == asset['material']
        assert not doc.get('images') and not doc.get('textures')
    for output in receipt['output_roster']:
        assert digest(STAGE / output['path']) == output['sha256'], output['path']
    for kind, stages in receipt['surfaces'].items():
        assert len(stages) == 3 and all(len(stage) == 4 and all(len(ring) == 32 for ring in stage) for stage in stages)
        assert all(value is not None and 0 < value < 2.5 for stage in stages for ring in stage for value in ring), kind


def install(receipt):
    validate(receipt)
    sys.path.insert(0, str(ROOT / 'art'))
    from hollow.godot import write_scene_import
    game = ROOT / 'game'
    destination = game / PACK
    destination.mkdir(parents=True, exist_ok=True)
    for output in receipt['output_roster']:
        source = STAGE / output['path']
        target = destination / output['path']
        target.parent.mkdir(parents=True, exist_ok=True)
        # Retain Godot's UID/import cache metadata on a repeat install.
        if target.exists() and target.suffix == '.import':
            continue
        shutil.copyfile(source, target)
    for asset in receipt['assets']:
        target = game / 'assets/world' / Path(asset['glb']).name
        shutil.copyfile(STAGE / asset['glb'], target)
        material = destination / 'Materials' / f"{asset['key']}-{asset['stage']}.tres"
        write_scene_import(target, {asset['material']: material})
    for name in ['README.md', 'ThirdParty/THIRD_PARTY_NOTICES.md', 'Reports/release_checksums.json']:
        target = destination / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(SOURCE / name, target)
    (destination / 'integration.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    path = game / 'data/ground/models.json'
    catalog = path.read_text(encoding='utf-8')
    for key in receipt['surfaces']:
        pattern = rf'(    \{{ "key": "{re.escape(key)}",.*?)(?=\n    \{{ "key"|\n\s*\])'
        matches = list(re.finditer(pattern, catalog, flags=re.S))
        assert len(matches) == 1
        old = matches[0].group(1)
        new = old.replace('"lods": 3,', '"lods": 4, "lodDistances": [14, 35, 90, 260],')
        new = re.sub(r'"source": "[^\"]+"', '"source": "PSG Resource Nodes R01: owner-authored geometry with Poly Haven CC0 detail; four LODs and three geometric depletion stages; assets/revised/PSG_ResourceNodes_R01/integration.json"', new)
        catalog = catalog.replace(old, new, 1)
    path.write_text(catalog, encoding='utf-8')
    path = game / 'data/ground/surfaces.json'
    surfaces = json.loads(path.read_text(encoding='utf-8'))
    assert surfaces['rings'] == [.12, .3, .48, .66] and surfaces['directions'] == 32
    surfaces['kinds'].update(receipt['surfaces'])
    surfaces['source'] = 'PSG node LOD0 surfaces measured from verified source geometry by tools/blender/resource_nodes.py: glTF Y-up, +X toward +Z, four rings by32 directions; root verifies with the game SurfaceSurvey after import. Values are metres above the shared stage pivot.'
    path.write_text(json.dumps(surfaces, separators=(',', ':')) + '\n', encoding='utf-8')
    print('INSTALLED twelve stage GLBs, twelve PBR materials,36 textures, four catalogue records and measured surface entries.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--install', action='store_true')
    args = parser.parse_args()
    if args.install:
        install(json.loads((STAGE / 'integration.json').read_text()))
    else:
        receipt = json.loads((STAGE / 'conversion.json').read_text())
        prepare(receipt)
        validate(receipt)
        print('VALIDATED local conversion is staged; no live game asset changed.')
