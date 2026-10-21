"""Capture one missing comparison still; deliberately avoid sequence rendering."""
from pathlib import Path
import numpy as np

out=Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur")
names={"Survivor-TPS-03-baseline-chop.casc":"baseline",
       "Survivor-TPS-04-candidate-chop.casc":"candidate"}
filename=Path(app.current_scene().get_path_name()).name
assert filename in names, filename
prefix=names[filename]
views=[(0,"full"),(13,"full"),(18,"full"),(21,"full"),
       (13,"grip"),(18,"grip"),(21,"grip"),(18,"side")]
pending=[(f,v,out / f"{prefix}-{v}-{f:02}.png") for f,v in views
         if not (out / f"{prefix}-{v}-{f:02}.png").exists()]
assert pending,"All comparison stills already exist"
frame,view,path=pending[0]
scene.set_current_frame(frame)
viewport=app.current_scene().active_viewport().domain_viewport()
camera=viewport.camera_struct()
if view=="grip":
    mv=scene.model_viewer();bv=mv.behaviour_viewer();dv=mv.data_viewer()
    wrist=mv.get_objects("hand_r")[0]
    transform=bv.get_behaviour_by_name(wrist,"Transform")
    p=dv.get_data_value(bv.get_behaviour_data(transform,"global_position"),frame)
    target=np.asarray(p)+np.array([0.0,3.0,4.0])
    camera.target=np.asarray(target,dtype=np.float32)
    camera.position=np.asarray(target+np.array([-75.0,35.0,95.0]),dtype=np.float32)
elif view=="side":
    camera.target=np.array([0.0,100.0,20.0],dtype=np.float32)
    camera.position=np.array([-400.0,150.0,25.0],dtype=np.float32)
else:
    camera.target=np.array([0.0,93.0,20.0],dtype=np.float32)
    camera.position=np.array([-250.0,160.0,360.0],dtype=np.float32)
viewport.set_camera_struct(camera)
viewport.set_mode_visualizers(csc.view.ViewportMode.View)
params=csc.tools.RenderParameters();params.width=1280;params.height=960;params.samples=4
renderer=next(t for t in app.get_tools_manager().tools() if isinstance(t,csc.tools.RenderToFile))
renderer.take_image(app.current_scene(),params,str(path))
print("Individual capture requested:",path)
