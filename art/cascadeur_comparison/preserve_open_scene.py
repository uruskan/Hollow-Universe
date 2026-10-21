"""Save the current in-memory Cascadeur state to a separate checkpoint."""
from pathlib import Path
import json

out = Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
target = out / 'previous-open-scene-preserved.casc'
assert not target.exists(), 'Never overwrite a preserved checkpoint'
source = app.current_scene().get_path_name()
app.current_scene().save(str(target))
assert target.exists() and target.stat().st_size > 1000
print(json.dumps({'previous_path': source, 'saved_copy': str(target), 'bytes': target.stat().st_size}))
