"""The monuments' scripts (art/monuments/sites/*.py, MONUMENTS.md §5): each defines KEY, TITLE, `build(b)` and
VIEWS (look-dev cameras), and optionally BUILDER (builder options), SUN, PEOPLE and SHIP for the renders.

    build.py -- [keys...] [--preview <folder>] [--export] [--fast]

With neither --preview nor --export a monument is exported. --fast renders at half size with fewer samples.
"""
import importlib.util
from pathlib import Path

from . import log
from .builder import Builder

SITES = Path(__file__).resolve().parents[1] / "sites"


def discover():
    mods = {}
    for f in sorted(SITES.glob("*.py")):
        if f.name.startswith("_"):
            continue
        spec = importlib.util.spec_from_file_location(f"hollow_site_{f.stem}", f)
        m = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(m)
        mods[m.KEY] = m
    return mods


def make(m):
    b = Builder(m.KEY, seed=getattr(m, "SEED", 1), title=getattr(m, "TITLE", m.KEY), **getattr(m, "BUILDER", {}))
    m.build(b)
    return b


def main(argv):
    from . import kit, preview
    keys, preview_dir, export, fast = [], None, False, False
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--preview":
            preview_dir = Path(argv[i + 1])
            i += 2
            continue
        if a == "--export":
            export = True
        elif a == "--fast":
            fast = True
        elif not a.startswith("--"):
            keys.append(a)
        i += 1
    if preview_dir is None:
        export = True
    mods = discover()
    for key in keys or list(mods):
        if key not in mods:
            raise SystemExit(f"no monument {key}; known: {', '.join(mods)}")
        m = mods[key]
        if export:
            preview.reset_scene()
            kit.EXPORT["enabled"] = True
            b = make(m)
            b.export(getattr(m, "LODS", None))
            kit.EXPORT["enabled"] = False
        if preview_dir is not None:
            for v in getattr(m, "VIEWS", [{"name": "front", "target": (0, 0, 5), "distance": 80, "yaw": 25,
                                           "pitch": 18}]):
                preview.reset_scene()
                b = make(m)
                preview.stage(b, sun=v.get("sun", getattr(m, "SUN", (28.0, 215.0))),
                              people=v.get("people", getattr(m, "PEOPLE", ())),
                              ship=v.get("ship", getattr(m, "SHIP", None)), fast=fast,
                              fog=v.get("fog", getattr(m, "FOG", 0.0)),
                              ground_size=getattr(m, "GROUND", 6000.0),
                              ao=getattr(m, "PREVIEW_AO", True))
                preview.camera(v["target"], v["distance"], v["yaw"], v["pitch"], v.get("lens", 35.0))
                preview.render(preview_dir / f"{key}-{v['name']}.png")
        log(f"monument {key} done")
