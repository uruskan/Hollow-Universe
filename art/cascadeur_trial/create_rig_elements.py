"""Validate the loaded QRT against source, then create its rig elements."""
import json
from pathlib import Path

out = Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur")
window = app.get_tools_manager().get_tool("RiggingToolWindowTool").editor(app.current_scene())
source = json.loads((out / "Survivor-TPS.qrigcasc").read_text(encoding="utf-8"))
loaded = json.loads(window.get_template_from_qrt())
def assignments(document):
    return {item["Bone name"]: (item["Joint name"], item["Joint path"])
            for group in document["Document"] for section in group["Sections"]
            for item in section["Names"] if item.get("Joint name")}
expected, actual = assignments(source), assignments(loaded)
assert len(expected) == 51
assert expected == actual, "Quick Rigging assignments changed"
before = len(scene.model_viewer().get_objects())
window.create_from_qrt_by_content(json.dumps(loaded))
after = len(scene.model_viewer().get_objects())
print("Mapped slots:", len(actual), "objects before/after:", before, after)
assert after > before, "No rig elements created; inspect event log"
print("Rig elements created; final controls still require generation.")
