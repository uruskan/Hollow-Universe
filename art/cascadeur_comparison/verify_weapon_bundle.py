"""Verify that the viewer publishes completed, validated native weapon takes."""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / '.artifacts/character-trial-cascadeur-v3-comparison'
NEW = ('heavyaxe_chop', 'pickaxe_swing', 'pistol_fire', 'pistol_reload', 'rifle_fire', 'rifle_reload')


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    metadata = read(OUT / 'candidates/metadata.json')['clips']['fps']
    viewer = read(OUT / 'review/manifest.json')
    assert set(viewer['names']) == {'hatchet_chop', 'torch_equip', *NEW}
    assert viewer['kinds'] == ['fps']
    rows = {}
    for clip in viewer['names']:
        candidate = OUT / 'candidates/fps' / (clip + '.mp4')
        packaged = OUT / 'review' / viewer['clips'][clip]['fps']['candidate']['video']['path']
        assert digest(candidate) == digest(packaged), (clip, 'stale packaged video')
        row = {'video_sha256': digest(candidate)}
        if clip in NEW:
            revision = metadata[clip]['revision']
            prefix = OUT / 'weapon-native' / (clip + '-' + revision)
            reports = {name: read(Path(str(prefix) + '-' + name + '.json')) for name in
                       ('validation', 'motion-validation', 'capture', 'media', 'reload')}
            assert reports['motion-validation']['passed'] and reports['reload']['passed'], clip
            assert reports['capture']['status'] == 'complete', clip
            assert reports['capture']['completed'] == reports['validation']['frames'], clip
            assert reports['media']['decode_passed'], clip
            assert reports['media']['sha256'] == digest(candidate), clip
            assert Path(metadata[clip]['native_scene']).is_file(), clip
            assert viewer['clips'][clip]['fps']['baseline']['video'] is None, clip
            row.update(revision=revision, frames=reports['validation']['frames'],
                       wrist_bend=reports['validation']['maximum_wrist_bend_degrees'],
                       source_manifest=str(prefix) + '-source-manifest.json')
        else:
            baseline = OUT / 'review' / viewer['clips'][clip]['fps']['baseline']['video']['path']
            original = ROOT / '.artifacts/character-trial-v3/videos/fps' / (clip + '.mp4')
            assert digest(baseline) == digest(original), clip
            row['baseline_sha256'] = digest(original)
        rows[clip] = row
    result = {'passed': True, 'candidate_count': len(rows), 'clips': rows,
              'browser_playback_verified': False,
              'limitations': 'Native motion and packaged media checks do not establish artistic acceptance.'}
    (OUT / 'weapon-bundle-validation.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
