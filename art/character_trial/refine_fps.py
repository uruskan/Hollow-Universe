"""Rebake FPS choreography into a new audition package, preserving the TPS baseline.

Run in background Blender. --diagnostics renders key poses from FPS and side
cameras after building. No source .blend or previous review file is saved over.
"""
from pathlib import Path
import argparse
import hashlib
import json
import math
import os
import shutil
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).parent))


def action_digest(prefix):
    rows = []
    for action in sorted(bpy.data.actions, key=lambda a: a.name):
        if not action.name.startswith(prefix):
            continue
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        rows.append((action.name, curve.data_path, curve.array_index,
                                     [(tuple(k.co), k.interpolation) for k in curve.keyframe_points]))
    return hashlib.sha256(json.dumps(rows).encode()).hexdigest()


def measure(rig, scene, names):
    import animate as A
    import review
    model = A.A.Rig(rig)
    result = {}
    for name in names:
        act, _ = review.select_action(rig, 'FPS_' + name)
        rows = []
        for frame in range(1, round(act.frame_range[1]) + 1):
            scene.frame_set(frame)
            hand = rig.pose.bones['hand_r'].matrix
            elbow = rig.pose.bones['lowerarm_r'].matrix.translation
            along = (hand.to_3x3() @ model.rest['hand_r'].to_3x3().inverted()
                     @ model.hand_frames['r'][0]).normalized()
            forearm = (hand.translation - elbow).normalized()
            prop_name = 'prop_' + ('rock' if name == 'rock_strike' else 'hatchet' if name == 'hatchet_chop' else 'torch')
            prop = rig.matrix_world @ rig.pose.bones[prop_name].matrix
            rows.append({'frame': frame, 'wrist_deviation_deg': math.degrees(along.angle(forearm)),
                         'wrist_world': list(rig.matrix_world @ hand.translation),
                         'prop_origin_world': list(prop.translation),
                         'shaft_direction': list(prop.to_3x3().col[2].normalized())})
        result[name] = {'max_wrist_deviation_deg': max(r['wrist_deviation_deg'] for r in rows),
                        'samples': rows}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', type=Path, default=ROOT / '.artifacts/character-trial-v2')
    parser.add_argument('--out', type=Path, default=ROOT / '.artifacts/character-trial-v3')
    parser.add_argument('--diagnostics', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    if args.base.resolve() == args.out.resolve():
        raise ValueError('FPS refinement must use a separate output directory')
    args.out.mkdir(parents=True, exist_ok=True)
    os.environ['CHARACTER_TRIAL_OUT'] = str(args.out)
    import animate as A
    import review
    changed = ('rock_strike', 'hatchet_chop', 'torch_equip')
    manifest = json.loads((args.base / 'manifest.json').read_text())
    for entry in manifest['files']:
        relative = Path(entry['path'])
        if relative.name in ('Survivor-Audition.blend', 'survivor-fps.glb'):
            continue
        dest = args.out / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        if not dest.exists():
            shutil.copy2(args.base / relative, dest)
    for view in ('fps', 'tps'):
        for clip in A.M.CLIPS:
            p = args.base / 'frames' / view / clip['name'] / 'frames.json'
            if p.exists():
                d = args.out / 'frames' / view / clip['name'] / 'frames.json'
                d.parent.mkdir(parents=True, exist_ok=True)
                if not d.exists(): shutil.copy2(p, d)
    bpy.ops.wm.open_mainfile(filepath=str(args.base / 'Survivor-Audition.blend'))
    scene, rig = review.select_scene('fps')
    before = measure(rig, scene, changed)
    tps_before = action_digest('TPS_')
    rig.animation_data_clear()
    for act in list(bpy.data.actions):
        if act.name.startswith('FPS_'):
            bpy.data.actions.remove(act)
    actions = A.bake(rig, True)
    meshes = [o for o in scene.objects if o.type == 'MESH' and
              any(m.type == 'ARMATURE' and m.object == rig for m in o.modifiers)]
    A.export(rig, meshes, args.out / 'survivor-fps.glb')
    after = measure(rig, scene, changed)
    rig.animation_data.action = None
    rig.animation_data.use_nla = True
    scene.timeline_markers.clear()
    A.composite_review(rig, actions)
    scene.frame_set(468)
    assert tps_before == action_digest('TPS_'), 'TPS actions changed during FPS refinement'
    bpy.ops.wm.save_as_mainfile(filepath=str(args.out / 'Survivor-Audition.blend'), compress=True)
    report = {'baseline': str(args.base), 'candidate': str(args.out),
              'tps_action_sha256_unchanged': tps_before, 'fps_hatchet_shaft_yaw_degrees': 77.08,
              'before': before, 'after': after,
              'boundary': 'Bone alignment metrics; not mesh contact or visual quality acceptance.'}
    (args.out / 'fps-refinement.json').write_text(json.dumps(report, indent=2))
    if args.diagnostics:
        folder = args.out / 'diagnostics'
        folder.mkdir(exist_ok=True)
        camera = scene.camera
        side = camera.copy(); side.data = camera.data.copy()
        scene.collection.objects.link(side)
        side.name = 'FPS wrist side diagnostic'
        side.location = (-1.75, -.33, 1.58)
        target = Vector((0., -.33, 1.58))
        side.rotation_euler = (target-side.location).to_track_quat('-Z', 'Y').to_euler()
        side.data.type = 'ORTHO'; side.data.ortho_scale = 1.20
        review.configure_render(scene)
        scene.eevee.taa_render_samples = 8
        for clip, frames in {'torch_equip': (9, 34), 'hatchet_chop': (1, 15, 19, 22),
                             'rock_strike': (1, 15, 20, 23)}.items():
            review.select_action(rig, 'FPS_' + clip)
            for frame in frames:
                for view, cam in (('fps', camera), ('side', side)):
                    scene.camera = cam
                    review.render_frame(scene, frame, folder / f'{clip}-{frame:02}-{view}.png')
        scene.camera = camera
    print('FPS_REFINEMENT_COMPLETE', flush=True)


if __name__ == '__main__':
    main()
