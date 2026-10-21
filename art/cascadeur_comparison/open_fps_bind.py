"""Open copied v3 FPS input in a fresh Cascadeur tab; preserve all existing tabs."""
from pathlib import Path

out = Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
source = out / 'inputs/survivor-fps-bind.fbx'
assert source.is_file()
manager = app.get_scene_manager()
fresh = manager.create_application_scene()
manager.set_current_scene(fresh)
loader = app.get_tools_manager().get_tool('FbxSceneLoader').get_fbx_loader(fresh)
loader.import_scene(str(source))
print('Imported separate FPS bind input; inspect next idle before rig generation.')
