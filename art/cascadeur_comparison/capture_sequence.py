"""Capture serial native stills, yielding to Cascadeur between every frame.

Avoids the application's image-sequence renderer, which crashed in the earlier
trial. Only one RenderToFile.take_image request can be outstanding at a time.
"""
import json
import time
from pathlib import Path
from PySide6.QtCore import QTimer

out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
view=app.current_scene();domain=view.domain_scene()
name=Path(view.get_path_name()).name
clip={'FPS-HatchetChop-Cascadeur-A02.casc':'hatchet_chop','FPS-TorchEquip-Cascadeur-A02.casc':'torch_equip'}[name]
folder=out/'candidates/fps'/(clip+'_frames')
assert not folder.exists(), 'Never mix capture runs'
folder.mkdir(parents=True)
state_path=out/(clip+'-capture-state.json')
params=csc.tools.RenderParameters();params.width=896;params.height=630;params.samples=2
renderer=next(t for t in app.get_tools_manager().tools() if isinstance(t,csc.tools.RenderToFile))
view.active_viewport().domain_viewport().set_mode_visualizers(csc.view.ViewportMode.View)
state={'clip':clip,'frames':domain.data_viewer().get_animation_size(),'completed':0,'status':'running','method':'Serial native Cascadeur viewport stills','resolution':[896,630]}
def persist():
    state_path.write_text(json.dumps(state,indent=2))
def fail(exc):
    state['status']='failed';state['error']=str(exc);persist()
def request_frame():
    try:
        domain.set_current_frame(state['completed'])
        QTimer.singleShot(100,take)
    except Exception as exc:fail(exc)
def take():
    try:
        state['current_path']=str(folder/('%04d.png'%state['completed']))
        state['request_time']=time.monotonic()
        renderer.take_image(view,params,state['current_path'])
        QTimer.singleShot(150,poll)
    except Exception as exc:fail(exc)
def poll():
    try:
        path=Path(state['current_path'])
        if path.is_file() and path.stat().st_size>100:
            with path.open('rb') as stream:
                stream.seek(-12,2);tail=stream.read()
            if tail[4:8]==b'IEND':
                state['completed']+=1;persist()
                if state['completed']==state['frames']:
                    state['status']='complete';persist();domain.set_current_frame(0)
                    print('Native capture complete',clip)
                else:QTimer.singleShot(50,request_frame)
                return
        if time.monotonic()-state['request_time']>8:
            raise RuntimeError('Native still did not complete within 8 seconds')
        QTimer.singleShot(100,poll)
    except Exception as exc:fail(exc)
persist();QTimer.singleShot(100,request_frame)
print('Serial native capture started',clip,state['frames'])
