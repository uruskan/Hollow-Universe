"""Verify saved candidates survive reload and close only task-owned scratch tabs."""
from pathlib import Path
import json
import numpy as np
from csc.view import camera_utils
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
manager=app.get_scene_manager()
results={}
for clip,filename in [('hatchet','FPS-HatchetChop-Cascadeur-A02.casc'),('torch','FPS-TorchEquip-Cascadeur-A02.casc')]:
    view=next(v for v in manager.scenes() if Path(v.get_path_name()).name==filename)
    manager.set_current_scene(view)
    csc.app.ProjectLoader.load_from(str(out/filename),view.domain_scene())
    domain=view.domain_scene();mv=domain.model_viewer();bv=mv.behaviour_viewer();dv=mv.data_viewer()
    expected=json.loads((out/(clip+'-A02-transforms.json')).read_text())
    maximum=0.;maximum_rot=0.
    joints={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Joint')}
    for n,o in joints.items():
        transform=bv.get_behaviour_by_name(o,'Transform')
        pos=bv.get_behaviour_data(transform,'global_position');rot=bv.get_behaviour_data(transform,'global_rotation')
        for f,pose in expected.items():
            maximum=max(maximum,float(np.linalg.norm(np.asarray(dv.get_data_value(pos,int(f)))-pose[n]['global_position'])))
            maximum_rot=max(maximum_rot,float(np.max(np.abs(dv.get_data_value(rot,int(f)).to_rotation_matrix()-np.asarray(pose[n]['global_rotation'])))))
    assert maximum<.01 and maximum_rot<.001,'Saved candidate changed during reload'
    camera=next(c for c in camera_utils.get_cameras(view) if c.name()=='FPS_review.001')
    camera_utils.set_camera_active(view.active_viewport(),camera)
    view.active_viewport().domain_viewport().set_mode_visualizers(csc.view.ViewportMode.View)
    domain.set_current_frame(0)
    view.save(view.get_path_name())
    results[clip]={'frames':len(expected),'maximum_reload_position_difference_cm':maximum,'maximum_reload_rotation_matrix_difference':maximum_rot,'passed':True}
scratch={'FPS-00-native-controls.casc','FPS-01-hatchet-v3-reference.casc','FPS-02-hand-controller-probe.casc','FPS-03-torch-v3-reference.casc','FPS-HatchetChop-Cascadeur-A01.casc','FPS-TorchEquip-Cascadeur-A01.casc'}
closed=[]
for view in list(manager.scenes()):
    path=Path(view.get_path_name())
    if path.parent==out and path.name in scratch:
        manager.remove_application_scene(view);closed.append(path.name)
hatchet=next(v for v in manager.scenes() if Path(v.get_path_name()).name=='FPS-HatchetChop-Cascadeur-A02.casc')
manager.set_current_scene(hatchet)
results['closed_saved_scratch_tabs']=closed
results['remaining_tabs']=[v.get_path_name() for v in manager.scenes()]
(out/'saved-candidates-validation.json').write_text(json.dumps(results,indent=2))
print(json.dumps(results))
