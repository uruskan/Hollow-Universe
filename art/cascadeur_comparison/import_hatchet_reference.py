"""Load v3 hatchet keys onto the isolated native control rig."""
from pathlib import Path
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
target=out/'FPS-01-hatchet-v3-reference.casc'
assert not target.exists()
assert app.current_scene().get_path_name().endswith('FPS-00-native-controls.casc')
loader=app.get_tools_manager().get_tool('FbxSceneLoader').get_fbx_loader(app.current_scene())
source=out/'inputs/survivor-fps-hatchet-chop.fbx'
print('Source takes:',loader.get_takes(str(source)))
loader.import_animation(str(source),0)
assert scene.data_viewer().get_animation_size()==43
app.current_scene().save(str(target))
print('Native v3 hatchet reference saved separately')
