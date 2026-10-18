"""Encode verified native still sequences and update review metadata."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'.artifacts/character-trial-cascadeur-v3-comparison'
FFMPEG=Path('D:/GameDev/Tools/ScreenCapture/ShareX/ffmpeg.exe')
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('clip');parser.add_argument('--revision',default='W01')
args=parser.parse_args()
prefix=OUT/'weapon-native'/(args.clip+'-'+args.revision)
state=json.loads(Path(str(prefix)+'-capture.json').read_text())
validation=json.loads(Path(str(prefix)+'-validation.json').read_text())
motion=json.loads(Path(str(prefix)+'-motion-validation.json').read_text())
plan=json.loads(Path(str(prefix)+'-plan.json').read_text())
assert motion['passed'], 'Failed motion diagnostics must not replace a review candidate'
assert state['status']=='complete' and state['completed']==state['frames']==validation['frames']
frames=Path(state.get('frame_folder',str(OUT/'candidates/fps'/(args.clip+'_'+args.revision+'_frames'))))
assert len(list(frames.glob('*.png')))==state['frames']
for f in range(state['frames']):
    data=(frames/('%04d.png'%f)).read_bytes()
    assert data[:8]==b'\x89PNG\r\n\x1a\n' and data[-8:-4]==b'IEND'
current_video=OUT/'candidates/fps'/(args.clip+'.mp4')
versions=OUT/'candidates/revisions';versions.mkdir(exist_ok=True)
video=versions/(args.clip+'-'+args.revision+'-take'+str(state.get('take',1))+'.mp4')
assert not video.exists(),'Never overwrite a versioned video'
subprocess.run([str(FFMPEG),'-hide_banner','-loglevel','error','-nostdin','-framerate','30','-i',str(frames/'%04d.png'),
    '-c:v','libx264','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(video)],check=True)
decoded=subprocess.run([str(FFMPEG),'-hide_banner','-loglevel','error','-nostdin','-i',str(video),'-f','null','-'],
    check=True,capture_output=True,text=True)
assert not decoded.stderr.strip(),decoded.stderr
if current_video.exists():
    previous=versions/(args.clip+'-previous-'+hashlib.sha256(current_video.read_bytes()).hexdigest()[:16]+'.mp4')
    if not previous.exists():shutil.copy2(current_video,previous)
shutil.copy2(video,current_video)
poses=plan['metadata']['poses']
if args.clip.endswith('_reload'):
    beat=next(int(f) for f,p in poses.items() if p['phase'] in ('seat magazine','seated'))
else:
    beat=next((int(f) for f,p in poses.items() if 'contact' in p['phase'] or 'recoil' in p['phase']),0)
shutil.copy2(frames/('%04d.png'%beat),current_video.with_suffix('.png'))
metadata_path=OUT/'candidates/metadata.json'
metadata=json.loads(metadata_path.read_text()) if metadata_path.exists() else {}
entry=metadata.setdefault('clips',{}).setdefault('fps',{}).setdefault(args.clip,{})
entry.update({'rendering_method':'Native Cascadeur viewport frames; sparse native IK poses and FK weapon parts',
    'notes':'New FPS weapon experiment; no Blender v3 baseline. Editable native scene retained. Visual acceptance remains yours.',
    'revision':args.revision,'native_scene':str(prefix)+'.casc','frames':state['frames'],'fps':30,
    'beat':beat/30.,'beat_label':'Jump to magazine seat' if args.clip.endswith('_reload') else 'Jump to recoil' if args.clip.endswith('_fire') else 'Jump to contact'})
if args.clip=='rifle_reload':
    entry['notes']+=' Tactical magazine reload: bolt stays closed; the charging-handle experiment is excluded.'
metadata_path.write_text(json.dumps(metadata,indent=2))
report={'clip':args.clip,'revision':args.revision,'frames':state['frames'],'video':str(video),'decode_passed':True,
        'sha256':hashlib.sha256(video.read_bytes()).hexdigest(),'still_frame':beat}
Path(str(prefix)+'-media.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
