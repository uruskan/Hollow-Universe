"""Shared code for Hollow Universe's Blender scripts (GROUND.md §9).

Everything here runs inside Blender 5.2 in background mode:

    blender -b --factory-startup --python art/build.py -- [names...]

Conventions every exported model follows (GROUND.md §9):
- metres; Blender +Z up becomes Godot +Y up;
- held items point along Blender +Y, which is Godot -Z (the camera's forward), with the pivot at the right hand's grip;
- world objects (deployables, doors, nodes, vehicles) face Blender -Y, which is Godot +Z (glTF's and Godot's
  "model front"), with the pivot on the ground at their centre (doors: at the lower hinge);
- LOD meshes are named `<Part>_LOD0..2`; convex collision hulls `<Part>_hull<i>-convcolonly`; attach points are
  empties named `socket_<name>`; parts that move (lids, doors, hatches, magazines) sit under an empty named after the
  part, at its pivot;
- GLB files carry no images: their materials are mapped to the shared Godot materials in `game/assets/materials` by
  the `.import` file written beside each GLB.
"""
from pathlib import Path
import os
import sys

REPO = Path(__file__).resolve().parents[2]
GAME = REPO / "game"
ASSETS = GAME / "assets"
ITEMS = ASSETS / "items"
WORLD = ASSETS / "world"
MATERIALS = ASSETS / "materials"
TEXTURES = ASSETS / "textures"

# Where the third-party kits live on the owner's PC. Override with environment variables on another machine.
SOURCES = Path(os.environ.get("HOLLOW_ASSET_SOURCES", r"D:\GameDev\Assets"))
RUSTMVP_ASSETS = Path(os.environ.get("HOLLOW_RUSTMVP_ASSETS", r"D:\GameDev\Projects\RustMVP\assets"))


def script_args():
    """The arguments after Blender's `--`."""
    argv = sys.argv
    return argv[argv.index("--") + 1:] if "--" in argv else []


def res_path(path):
    """A Godot resource path (res://...) for a file inside game/."""
    return "res://" + Path(path).resolve().relative_to(GAME.resolve()).as_posix()


def log(*parts):
    print("[hollow]", *parts, flush=True)
