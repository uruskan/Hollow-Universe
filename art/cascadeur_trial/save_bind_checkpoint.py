"""Save the fresh imported bind through the documented scene save method."""
from pathlib import Path

path = Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur/Survivor-TPS-00-imported-bind.casc")
assert not path.exists(), "Never overwrite an existing checkpoint"
view = app.current_scene()
assert len(view.domain_scene().behaviour_viewer().get_behaviours("Joint")) == 54
view.save(str(path))
print("Native scene save requested:", path)
print("Verify filesystem after the next idle event; this API returns None.")
