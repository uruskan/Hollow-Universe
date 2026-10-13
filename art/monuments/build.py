"""Builds Hollow's monuments (MONUMENTS.md §8) in one Blender process.

    blender -b --factory-startup --python art/monuments/build.py                      # every monument
    blender -b --factory-startup --python art/monuments/build.py -- grid-substation   # some
    blender -b --factory-startup --python art/monuments/build.py -- --surfaces [--force]   # the texture sets only
    blender -b --factory-startup --python art/monuments/build.py -- grid-substation --preview docs/images/monuments

`--preview <folder>` renders each monument's look-dev views there instead of (or as well as, with --export) writing
the game files.
"""
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent))

import monkit  # noqa: E402,F401
from monkit import surfaces  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
flags = {a for a in argv if a.startswith("--")}

if "--surfaces" in flags:
    names = [a for a in argv if not a.startswith("--")]
    surfaces.build_textures(names or None, force="--force" in flags)
else:
    from monkit import sites
    sites.main(argv)
