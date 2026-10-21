"""Generate final controls, checking callback result and bind-pose drift."""
import json
from pathlib import Path
import numpy as np
import rig_mode.off as rig_off
from rig_mode.camera_context import Camera_context

out = Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur")
path = out / "Survivor-TPS-02-controls-no-autoposing.casc"
assert not path.exists()
window = app.get_tools_manager().get_tool("RiggingToolWindowTool").editor(app.current_scene())
window.set_is_create_autoposing(False)
def joint_positions():
    mv = scene.model_viewer(); bv = mv.behaviour_viewer(); dv = mv.data_viewer()
    return {mv.get_object_name(bv.get_behaviour_owner(b)):
            dv.get_data_value(bv.get_behaviour_data(bv.get_behaviour_by_name(
                bv.get_behaviour_owner(b), "Transform"), "global_position"), 0).copy()
            for b in bv.get_behaviours("Joint")}
assert len(scene.behaviour_viewer().get_behaviours("AnimationInfo")) == 1
before = joint_positions()
undo_context, redo_context = Camera_context(), Camera_context()
ok = scene.modify_update_with_session(
    "Cascadeur trial: generate controls",
    lambda model, update, updater, session:
        rig_off.off(scene, model, update, updater, session, True, False,
                    undo_context, redo_context))
print("Generate controls modification:", ok)
if not ok:
    raise RuntimeError("Control generation failed; inspect event log")
after = joint_positions()
bv = scene.behaviour_viewer()
assert len(bv.get_behaviours("RigInfo")) == 1
assert not bv.get_behaviours("AnimationInfo")
assert set(before) == set(after)
drift = {name: float(np.linalg.norm(after[name] - before[name])) for name in before}
report = {"callback_ok": ok, "create_autoposing": False, "joints_retained": len(after),
          "maximum_joint_drift_cm": max(drift.values()), "joint_drift_cm": drift}
(out / "control-generation-no-autoposing-validation.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps({k: v for k, v in report.items() if k != "joint_drift_cm"}))
app.current_scene().save(str(path))
params = csc.tools.RenderParameters(); params.width = 1600; params.height = 1000; params.samples = 4
renderer = next(t for t in app.get_tools_manager().tools() if isinstance(t, csc.tools.RenderToFile))
app.current_scene().active_viewport().domain_viewport().set_mode_visualizers(csc.view.ViewportMode.PointController)
renderer.take_image(app.current_scene(), params, str(out / "02-controls-no-autoposing.png"))
