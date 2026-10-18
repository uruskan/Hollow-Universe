"""Capture baseline contact using a repeatable native camera."""
from pathlib import Path
import numpy as np

out = Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur")
scene.set_current_frame(18)
viewport = app.current_scene().active_viewport().domain_viewport()
camera = viewport.camera_struct()
camera.position = np.array([-250.0, 160.0, 360.0], dtype=np.float32)
camera.target = np.array([0.0, 93.0, 20.0], dtype=np.float32)
viewport.set_camera_struct(camera)
viewport.set_mode_visualizers(csc.view.ViewportMode.Mesh)
params = csc.tools.RenderParameters(); params.width = 1280; params.height = 960; params.samples = 4
renderer = next(t for t in app.get_tools_manager().tools() if isinstance(t, csc.tools.RenderToFile))
renderer.take_image(app.current_scene(), params, str(out / "03-baseline-contact.png"))
print("Baseline contact capture requested at frame 18.")
