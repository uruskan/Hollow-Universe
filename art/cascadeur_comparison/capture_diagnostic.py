"""Capture a diagnostic viewport using Cascadeur's supported spherical camera."""
from pathlib import Path
import numpy as np
from csc.view import camera_utils
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
view=app.current_scene()
print('Current scene',view.get_path_name(),'frame',scene.get_current_frame())
camera_utils.reset_camera(view.active_viewport())
vp=view.active_viewport().domain_viewport()
camera=vp.camera_struct()
camera.position=np.array([0.,168.,4.],dtype=np.float32)
camera.target=np.array([0.,147.44828,101.86535],dtype=np.float32)
vp.set_camera_struct(camera)
vp.set_mode_visualizers(csc.view.ViewportMode.Mesh)
params=csc.tools.RenderParameters();params.width=1280;params.height=960;params.samples=4
renderer=next(t for t in app.get_tools_manager().tools() if isinstance(t,csc.tools.RenderToFile))
renderer.take_image(view,params,str(out/'FPS-hatchet-diagnostic.png'))
print('Diagnostic requested')
