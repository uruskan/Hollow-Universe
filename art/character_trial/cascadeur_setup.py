"""Build the audition's Cascadeur Quick Rigging mapping from its exported skeleton.

The JSON schema and slot names follow Cascadeur 2026.2's bundled templates.
This contains only our character's joint mapping, not a sample character asset.

Run with normal Python to write Survivor-TPS.qrigcasc. In Cascadeur, import that
file through Quick Rigging Tool's menu, or call load_current() from its Python
console after importing Survivor-TPS-bind.fbx and entering Rig mode. Loading
the mapping does not generate a rig, alter animation, import assets, or save.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT = ROOT / ".artifacts" / "character-trial-cascadeur"
TEMPLATE_NAME = "Survivor-TPS.qrigcasc"


def build_template(skeleton: dict) -> dict:
    """Map 21 body slots and all 30 finger joints; leave absent twists empty."""
    parents = {bone["name"]: bone["parent"] for bone in skeleton["bones"]}
    if len(parents) != len(skeleton["bones"]):
        raise ValueError("Duplicate joint names in exported skeleton")

    def joint_path(name: str) -> list[str]:
        path = []
        seen = {name}
        current = parents[name]
        while current is not None:
            if current in seen:
                raise ValueError(f"Cyclic joint hierarchy at {name}")
            seen.add(current)
            path.append(current)
            current = parents[current]
        return list(reversed(path))

    def section(title: str, pairs: list[tuple[str, str]]) -> dict:
        return {
            "Section": title,
            "Names": [
                {"Bone name": slot, "Joint name": joint,
                 "Joint path": joint_path(joint)}
                for slot, joint in pairs
            ],
        }

    body = [section("Body", [
        ("pelvis", "pelvis"),
        ("stomach", "spine_01"),
        ("chest", "spine_02"),
        ("neck", "neck_01"),
        ("head", "head"),
    ])]
    for label, side in (("Left", "l"), ("Right", "r")):
        body.append(section(f"{label} arm", [
            (f"clavicle_{side}", f"clavicle_{side}"),
            (f"arm_{side}", f"upperarm_{side}"),
            (f"forearm_{side}", f"lowerarm_{side}"),
            (f"hand_{side}", f"hand_{side}"),
        ]))
    for label, side in (("Left", "l"), ("Right", "r")):
        body.append(section(f"{label} leg", [
            (f"thigh_{side}", f"thigh_{side}"),
            (f"calf_{side}", f"calf_{side}"),
            (f"foot_{side}", f"foot_{side}"),
            (f"toe_{side}", f"ball_{side}"),
        ]))
    document = [{"Title": "Body", "Sections": body}]
    finger_slots = (("Thumb", "thumb", "thumb"),
                    ("Index finger", "index_finger", "index"),
                    ("Middle finger", "middle_finger", "middle"),
                    ("Ring finger", "ring_finger", "ring"),
                    ("Pinky", "pinky", "pinky"))
    for label, side in (("Left", "l"), ("Right", "r")):
        document.append({"Title": f"{label} hand", "Sections": [
            section(name, [(f"{slot}_{side}_{i}", f"{joint}_{i:02}_{side}")
                           for i in range(1, 4)])
            for name, slot, joint in finger_slots
        ]})
    document.append({"Title": "Twist bones", "Sections": []})
    return {"Document": document,
            "Settings": {"Is align pelvis": True, "Is create layers": True}}


def load_current(template_path: str | Path | None = None) -> None:
    """Load our mapping into an already-open Cascadeur Quick Rigging Tool.

    Uses shipped csc.tools.RiggingWindow APIs. Does not generate controls:
    inspect mapping first, then use Add rig elements and Generate rig in UI.
    """
    import csc

    path = Path(template_path) if template_path else DEFAULT_OUT / TEMPLATE_NAME
    data = json.loads(path.read_text(encoding="utf-8"))
    required = {entry["Joint name"] for group in data["Document"]
                for part in group["Sections"] for entry in part["Names"]}
    app = csc.app.get_application()
    app_scene = app.get_scene_manager().current_scene()
    if app_scene is None:
        raise RuntimeError("Open a scene and import the TPS bind FBX first")
    scene = app_scene.domain_scene()
    model = scene.model_viewer()
    behaviours = model.behaviour_viewer()
    names = {model.get_object_name(behaviours.get_behaviour_owner(joint))
             for joint in behaviours.get_behaviours("Joint")}
    missing = sorted(required - names)
    if missing:
        raise RuntimeError("Mapping cannot load; missing joints: " + ", ".join(missing))
    window = app.get_tools_manager().get_tool("RiggingToolWindowTool").editor(app_scene)
    window.open_quick_rigging_tool()
    window.load_template_by_fileName(str(path))
    scene.info("Survivor mapping loaded: 21 body and 30 finger slots. Inspect before generating.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    args = parser.parse_args()
    skeleton = json.loads((args.out / "skeleton-rest.json").read_text(encoding="utf-8"))
    template = build_template(skeleton)
    path = args.out / TEMPLATE_NAME
    path.write_text(json.dumps(template, indent=2) + "\n", encoding="utf-8")
    print(path)


if __name__ == "__main__":
    main()
