"""Hollow's monument kit (MONUMENTS.md §8): tileable surfaces, a builder for monuments made of kit pieces and their
own geometry, the kit itself, and look-dev renders. Runs inside Blender 5.2 in background mode, through
art/monuments/build.py.
"""
import sys
from pathlib import Path

ART = Path(__file__).resolve().parents[2]
if str(ART) not in sys.path:
    sys.path.insert(0, str(ART))

from hollow import ASSETS, GAME, log  # noqa: E402

MONUMENTS = ASSETS / "monuments"
KIT = MONUMENTS / "kit"
DATA = GAME / "data" / "monuments"

__all__ = ["ART", "ASSETS", "GAME", "MONUMENTS", "KIT", "DATA", "log"]
