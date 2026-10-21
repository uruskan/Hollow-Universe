"""Enter Rig mode with checked callbacks and load the existing mapping only."""
import json
from pathlib import Path
import rig_mode.on as rig_on
from rig_mode.camera_context import Camera_context

out = Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur")
assert (out / "Survivor-TPS-00-imported-bind.casc").is_file()
bv = scene.behaviour_viewer()
assert not bv.get_behaviours("AnimationInfo"), "Already in rig mode"
assert not bv.get_behaviours("RigInfo"), "Already contains a rig"
assert len(bv.get_behaviours("Joint")) == 54
undo_context, redo_context = Camera_context(), Camera_context()
ok = scene.modify_update_with_session(
    "Cascadeur trial: enter Rig mode",
    lambda model, update, updater, session:
        rig_on.on(model, update, updater, session, [0.0, 0.5, 0.0],
                  None, None, undo_context, redo_context))
print("Rig mode modification:", ok)
if not ok:
    raise RuntimeError("Rig mode failed; inspect event log")
assert len(scene.behaviour_viewer().get_behaviours("AnimationInfo")) == 1
window = app.get_tools_manager().get_tool("RiggingToolWindowTool").editor(app.current_scene())
window.open_quick_rigging_tool()
window.load_template_by_fileName(str(out / "Survivor-TPS.qrigcasc"))
loaded = window.get_template_from_qrt()
(out / "quickrig-loaded.json").write_text(loaded, encoding="utf-8")
print("Loaded template bytes:", len(loaded))
print("Controls not yet generated; inspect loaded mapping first.")
