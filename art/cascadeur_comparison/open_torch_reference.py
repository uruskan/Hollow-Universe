"""Import torch v3 onto a fresh copy of the native FPS controls."""
from pathlib import Path
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
target=out/'FPS-03-torch-v3-reference.casc'
assert not target.exists()
manager=app.get_scene_manager()
view=manager.create_application_scene();manager.set_current_scene(view)
csc.app.ProjectLoader.load_from(str(out/'FPS-00-native-controls.casc'),view.domain_scene())
loader=app.get_tools_manager().get_tool('FbxSceneLoader').get_fbx_loader(view)
loader.import_animation(str(out/'inputs/survivor-fps-torch-equip.fbx'),0)
assert view.domain_scene().data_viewer().get_animation_size()==49
view.save(str(target))
print('Separate torch reference saved')
