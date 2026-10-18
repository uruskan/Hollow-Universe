"""Verify the original comparison's immutable Blender baseline receipt."""
from pathlib import Path
import hashlib
import json

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'.artifacts/character-trial-cascadeur-v3-comparison'
expected=json.loads((OUT/'preserved-baselines.json').read_text())['files']
failures=[]
for relative,entry in expected.items():
    path=ROOT/relative
    if not path.is_file():failures.append({'path':relative,'reason':'missing'});continue
    digest=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):digest.update(block)
    if digest.hexdigest()!=entry['sha256'] or path.stat().st_size!=entry['bytes']:
        failures.append({'path':relative,'reason':'hash or byte count changed'})
report={'files_checked':len(expected),'unchanged':not failures,'failures':failures}
(OUT/'weapon-baseline-verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
raise SystemExit(bool(failures))
