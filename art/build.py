"""Rebuilds every model of the ground game (GROUND.md §9) in one Blender process.

    blender -b --factory-startup --python art/build.py              # everything
    blender -b --factory-startup --python art/build.py -- tools     # one script: starter_kit, frontier_rifle,
                                                                     # tin_finch, iron_wake, resources, tools, supplies,
                                                                     # clothing, deployables, doors, nodes, attachments,
                                                                     # props, raid, animal_items, ka_fa_1500, togg, zamazingo

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
    "iron_wake": "import/iron_wake.py",
    "resources": "models/resources.py",
    "cloth": "models/cloth.py",
    "sewing_kit": "models/sewing_kit.py",
    "metal_blade": "models/metal_blade.py",
    "sign_plate": "models/sign_plate.py",
    "receiver": "models/receiver.py",
    "rifle_frame": "models/rifle_frame.py",
    "torn_schematic": "models/torn_schematic.py",
    "sealed_schematic": "models/sealed_schematic.py",
    "salvage": "models/salvage.py",
    "explosives": "models/explosives.py",
    "blueprint": "models/blueprint.py",
    "research_table": "models/research_table.py",
    "wire_tool": "models/wire_tool.py",
    "motorboat": "models/motorboat.py",
    "tools": "models/tools.py",
    "supplies": "models/supplies.py",
    "clothing": "models/clothing.py",
    "garment_items": "models/garment_items.py",
    "deployables": "models/deployables.py",
    "doors": "models/doors.py",
    "nodes": "models/nodes.py",
    "attachments": "models/attachments.py",
    "props": "models/props.py",
    "raid": "models/raid.py",
    "animal_items": "models/animal_items.py",
    "ka_fa_1500": "models/ka_fa_1500.py",
    "togg": "models/togg.py",
    "zamazingo": "models/zamazingo.py",
}

wanted = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
for name, script in SCRIPTS.items():
    if wanted and name not in wanted:
        continue
    print(f"[hollow] === {script}", flush=True)
    # Each script reads its own arguments after "--": none means all of its models.
    sys.argv = [sys.argv[0], "--"]
    runpy.run_path(str(HERE / script), run_name="__main__")
