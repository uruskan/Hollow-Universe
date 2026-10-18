"""Probe documented timeline extension on a disposable copy, never a baseline."""
from pathlib import Path
import json

out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
manager=app.get_scene_manager()
previous=app.current_scene()
view=manager.create_application_scene();manager.set_current_scene(view)
csc.app.ProjectLoader.load_from(str(out/'FPS-HatchetChop-Cascadeur-A02.casc'),view.domain_scene())
domain=view.domain_scene();lv=domain.layers_viewer()
mv=domain.model_viewer();bv=mv.behaviour_viewer()
objects=[bv.get_behaviour_owner(b) for b in bv.get_behaviours('Joint')]
layers=set(lv.layer_ids_by_obj_ids(objects))
before=domain.data_viewer().get_animation_size()
def edit(model,update,updater,session):
    editor=model.layers_editor()
    for layer in layers:
        editor.set_fixed_interpolation_or_key_if_need(layer,80,True)
    model.fit_animation_size_by_layers()
    editor.normalize_sections(domain)
    interpolator=updater.get_interpolator();interpolator.reload();interpolator.interpolate()
ok=domain.modify_update_with_session('Probe timeline extension in disposable copy',edit)
after=domain.data_viewer().get_animation_size()
result={'ok':bool(ok),'before':before,'after':after}
(out/'timeline-extension-probe.json').write_text(json.dumps(result))
print(json.dumps(result))
manager.remove_application_scene(view);manager.set_current_scene(previous)
assert ok and after==81
