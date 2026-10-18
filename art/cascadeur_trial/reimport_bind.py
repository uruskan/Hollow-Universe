"""Import the preserved bind FBX in a fresh tab, retaining the prior tab."""
from pathlib import Path

out = Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur")
source = out / "survivor-tps-bind.fbx"
assert source.is_file()
manager = app.get_scene_manager()
fresh = manager.create_application_scene()
manager.set_current_scene(fresh)
loader = app.get_tools_manager().get_tool("FbxSceneLoader").get_fbx_loader(fresh)
loader.import_scene(str(source))
print("Import dispatched; inspect counts and log in a later idle request.")
