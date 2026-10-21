"""After first Godot import, configure only the geology catalog's real dependencies.
Run with normal Python, then run Godot --headless --import again.
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GAME = ROOT/'game'


def set_params(path, values):
    text = path.read_text(encoding='utf8')
    for key, value in values.items():
        pattern = re.compile(r'^' + re.escape(key) + r'=.*$', re.M)
        assert pattern.search(text), (path, key)
        text = pattern.sub(key + '=' + value, text)
    path.write_text(text, encoding='utf8')


def main():
    rock = json.loads((GAME/'assets/surface/rocks/catalog.json').read_text())
    cliffs = json.loads((GAME/'assets/surface/cliffs/provenance.json').read_text())
    models = [GAME/rock['model'][6:]] + [ROOT/o['file'] for row in cliffs['assets'] for o in row['outputs']]
    images, normals = set(), set()
    for model in models:
        data = json.loads(model.read_text())
        for image in data.get('images', []): images.add(model.parent/image['uri'])
        for material in data.get('materials', []):
            if 'normalTexture' in material:
                index = data['textures'][material['normalTexture']['index']]['source']
                normals.add(model.parent/data['images'][index]['uri'])
        set_params(Path(str(model)+'.import'), {'meshes/generate_lods':'false', 'meshes/light_baking':'0'})
    for path in images:
        set_params(Path(str(path)+'.import'), {'compress/mode':'2', 'mipmaps/generate':'true',
            'detect_3d/compress_to':'0', 'compress/normal_map':'1' if path in normals else '2'})
    print('Configured', len(models), 'models and', len(images), 'shared GPU-compressed textures;',len(normals),'normal maps')


if __name__ == '__main__': main()
