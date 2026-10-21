"""Render the current known comparison scene to native PNG frames."""
from pathlib import Path
import numpy as np

out = Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur")
filename = Path(app.current_scene().get_path_name()).name
names = {"Survivor-TPS-03-baseline-chop.casc": "baseline-frames",
         "Survivor-TPS-04-candidate-chop.casc": "candidate-frames"}
assert filename in names, filename
folder = out / names[filename]
assert not folder.exists(), "Do not overwrite a rendered comparison"
folder.mkdir()
boundary=app.current_scene().animation_boundary()
boundary.first_frame=0;boundary.last_frame=42
viewport=app.current_scene().active_viewport().domain_viewport()
camera=viewport.camera_struct()
camera.position=np.array([-250.0,160.0,360.0],dtype=np.float32)
camera.target=np.array([0.0,93.0,20.0],dtype=np.float32)
viewport.set_camera_struct(camera)
viewport.set_mode_visualizers(csc.view.ViewportMode.View)
params=csc.tools.RenderParameters();params.width=960;params.height=720;params.samples=4
renderer=next(t for t in app.get_tools_manager().tools() if isinstance(t,csc.tools.RenderToFile))
renderer.play_to_images_sequence(app.current_scene(),params,str(folder))
print("Requested frames 0..42:",folder)
