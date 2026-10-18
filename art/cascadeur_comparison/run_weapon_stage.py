"""Submit an explicit native weapon stage with its arguments in the receipt."""
from pathlib import Path
import argparse
import hashlib
import json
import subprocess
import sys

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('stage',choices=('open_bind','rig_controls','import_ready','snapshot_ready','author','capture','finish'))
parser.add_argument('weapon')
parser.add_argument('--clip')
parser.add_argument('--revision')
parser.add_argument('--receipt',required=True)
args=parser.parse_args()
scripts=Path(__file__).resolve().parent
native=scripts.parents[1]/'.artifacts/character-trial-cascadeur-v3-comparison/weapon-native'
for capture in native.glob('*-capture.json'):
    assert json.loads(capture.read_text())['status']!='running', f'Wait for native capture: {capture.name}'
code='from pathlib import Path\nimport hashlib\n'
code+=f"p=Path({str(scripts/'weapons_native.py')!r})\n"
source=(scripts/'weapons_native.py').read_bytes()
code+="native_code=p.read_bytes()\n"
code+=f"assert hashlib.sha256(native_code).hexdigest()=={hashlib.sha256(source).hexdigest()!r}\n"
if args.stage=='author':
    planner=scripts/'weapons_pose_targets.py'
    code+=f"assert hashlib.sha256(Path({str(planner)!r}).read_bytes()).hexdigest()=={hashlib.sha256(planner.read_bytes()).hexdigest()!r}\n"
code+="exec(compile(native_code,str(p),'exec'),globals())\n"
params=[args.weapon]
if args.stage in ('author','capture','finish'):
    if not args.clip:parser.error('--clip is required for this stage')
    params.extend([args.clip,args.revision or 'W01'])
elif args.stage in ('import_ready','snapshot_ready') and args.revision:
    params.append(args.revision)
code+=f"{args.stage}({', '.join(repr(p) for p in params)})\n"
request=scripts/'weapon_stage_request.py'
request.write_text(code,encoding='utf-8')
raise SystemExit(subprocess.call([sys.executable,str(scripts/'mcp_client.py'),str(request),'--receipt',args.receipt]))
