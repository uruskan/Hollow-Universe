"""Generate native controls in the separate imported FPS bind scene."""
import json
from pathlib import Path
import numpy as np
import rig_mode.on as rig_on
import rig_mode.off as rig_off
from rig_mode.camera_context import Camera_context

out = Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
target = out / 'FPS-00-native-controls.casc'
assert not target.exists()
mv=scene.model_viewer(); bv=mv.behaviour_viewer(); dv=mv.data_viewer()
assert len(bv.get_behaviours('Joint')) == 56
assert not bv.get_behaviours('RigInfo') and not bv.get_behaviours('AnimationInfo')
def positions():
    return {mv.get_object_name(bv.get_behaviour_owner(b)): dv.get_data_value(
        bv.get_behaviour_data(bv.get_behaviour_by_name(bv.get_behaviour_owner(b), 'Transform'), 'global_position'),0).copy()
        for b in bv.get_behaviours('Joint')}
before=positions()
assert scene.modify_update_with_session('Comparison: enter rig mode',
    lambda model, update, updater, session: rig_on.on(model,update,updater,session,
        [0.0,0.5,0.0],None,None,Camera_context(),Camera_context()))
window=app.get_tools_manager().get_tool('RiggingToolWindowTool').editor(app.current_scene())
window.open_quick_rigging_tool()
template=out/'inputs/Survivor-FPS.qrigcasc'
window.load_template_by_fileName(str(template))
loaded=json.loads(window.get_template_from_qrt())
def mapped(doc):
    return {i['Bone name']:(i['Joint name'],i['Joint path']) for g in doc['Document']
            for s in g['Sections'] for i in s['Names'] if i.get('Joint name')}
assert mapped(loaded)==mapped(json.loads(template.read_text(encoding='utf-8')))
assert len(mapped(loaded))==51
window.create_from_qrt_by_content(json.dumps(loaded))
assert bv.get_behaviours('AnimationInfo')
window.set_is_create_autoposing(False)
assert scene.modify_update_with_session('Comparison: generate native controls',
    lambda model, update, updater, session: rig_off.off(scene,model,update,updater,
        session,True,False,Camera_context(),Camera_context()))
assert len(bv.get_behaviours('RigInfo'))==1
after=positions()
drift=max(float(np.linalg.norm(after[n]-v)) for n,v in before.items())
print('Control generation maximum bind drift, cm:',drift)
assert drift<0.1
app.current_scene().save(str(target))
print('Saved separate native FPS control rig:',target)
