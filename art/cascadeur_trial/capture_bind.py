"""Capture the current Cascadeur viewport through its native render tool."""
from pathlib import Path

path = Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur/00-imported-bind.png")
assert not path.exists()
renderer = next(tool for tool in app.get_tools_manager().tools()
                if isinstance(tool, csc.tools.RenderToFile))
params = csc.tools.RenderParameters()
params.width = 1600
params.height = 1000
params.samples = 4
renderer.take_image(app.current_scene(), params, str(path))
print("Native viewport image requested:", path)
