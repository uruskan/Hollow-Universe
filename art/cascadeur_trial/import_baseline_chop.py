"""Import the existing chop onto the clean rig and save a separate baseline."""
from pathlib import Path

out = Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur")
target = out / "Survivor-TPS-03-baseline-chop.casc"
assert not target.exists()
assert app.current_scene().get_path_name().endswith("Survivor-TPS-02-controls-no-autoposing.casc")
loader = app.get_tools_manager().get_tool("FbxSceneLoader").get_fbx_loader(app.current_scene())
source = str(out / "survivor-tps-hatchet-chop.fbx")
print("Available takes:", loader.get_takes(source))
loader.import_animation(source, 0)
print("Frames after import:", scene.data_viewer().get_animation_size())
app.current_scene().save(str(target))
