"""Strip Mine, stone (MONUMENTS.md §5.2 no. 20, small tier): the stone variant of the bench mine; the design is in
_strip_mine.py, shared by the three variants.
"""
import importlib.util
from pathlib import Path

_spec = importlib.util.spec_from_file_location("hollow_site__strip_mine", Path(__file__).with_name("_strip_mine.py"))
_mine = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_mine)

KEY = "strip-mine-stone"
TITLE = "Strip Mine (Stone)"
SEED = 2001
BUILDER = {"chunk": 40.0, "max_edge": 3.0}


def build(b):
    _mine.build_mine(b, "stone")


SUN = _mine.SUN
PEOPLE = _mine.PEOPLE
VIEWS = _mine.VIEWS
