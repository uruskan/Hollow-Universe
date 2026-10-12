"""Rebuilds every model of the ground game (GROUND.md §9) in one Blender process.

    blender -b --factory-startup --python art/build.py              # everything
    blender -b --factory-startup --python art/build.py -- tools     # one script: starter_kit, frontier_rifle,
                                                                     # tin_finch, resources, tools, supplies, clothing,
                                                                     # deployables, nodes, attachments, props

Then render the icons with Godot (a real window, not --headless):

    Godot --path game -- --view icons --out game/assets/items/icons

Textures already converted are kept; delete game/assets/textures/<set> to convert them again.
"""
import runpy
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCRIPTS = {
    "starter_kit": "import/starter_kit.py",
    "frontier_rifle": "import/frontier_rifle.py",
    "tin_finch": "import/tin_finch.py",
    "resources": "models/resources.py",
    "tools": "models/tools.py",
    "supplies": "models/supplies.py",
    "clothing": "models/clothing.py",
    "deployables": "models/deployables.py",
    "nodes": "models/nodes.py",
    "attachments": "models/attachments.py",
    "props": "models/props.py",
}

wanted = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
for name, script in SCRIPTS.items():
    if wanted and name not in wanted:
        continue
    print(f"[hollow] === {script}", flush=True)
    # Each script reads its own arguments after "--": none means all of its models.
    sys.argv = [sys.argv[0], "--"]
    runpy.run_path(str(HERE / script), run_name="__main__")
