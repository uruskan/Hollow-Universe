"""Rebuild changed footstep recipes through the shared encoder, without enumerating unrelated synthesis.

    python -I art/audio/repair_footsteps.py

The full build.py path uses the identical recipes. Existing playback settings and all
non-footstep catalogue/measurement entries are preserved, as in repair_actions.py.
"""
import os
import sys
import inspect  # Load stdlib before the adjacent legacy inspect.py can shadow it.
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build
import events


def main():
    events.footsteps()
    levels = build.load_json(build.LEVELS, {"files": {}})["files"]
    changed = 0
    for output in events.OUTS:
        path = os.path.join(build.ASSETS, output.rel)
        prior = levels.get(output.rel)
        if prior and prior.get("recipe") == output.recipe() and os.path.isfile(path) and prior.get("sha256") == build.sha256(path):
            continue
        measured = build.build(output)
        metric, _, low, high, _ = build.CATEGORIES[output.category]
        if not low <= measured[metric] <= high or measured["truePeak"] > build.TRUE_PEAK_CEILING:
            raise RuntimeError(f"{output.rel}: measured audio outside category limits")
        levels[output.rel] = measured
        changed += 1
        print(f"{output.rel}: {measured[metric]:.2f} LUFS, {measured['truePeak']:.2f} dBTP", flush=True)
    build.write_levels(levels)
    replacements = {event["key"]: event for event in events.EVENTS}
    old = build.load_json(build.CATALOGUE, {})["events"]
    ordered = [replacements.pop(event["key"], event) for event in old]
    build.write_catalogue(ordered + list(replacements.values()), events.ITEMS, events.SPACES)
    current = {output.rel for output in events.OUTS}
    root = Path(build.ASSETS, "footstep").resolve()
    removed = 0
    for relative in list(levels):
        if not relative.startswith("footstep/") or relative in current:
            continue
        path = Path(build.ASSETS, relative).resolve()
        if not path.is_relative_to(root) or path.suffix != ".ogg":
            raise RuntimeError(f"unsafe obsolete footstep path: {path}")
        path.unlink(missing_ok=True)
        Path(str(path) + ".import").unlink(missing_ok=True)
        del levels[relative]
        removed += 1
    build.write_levels(levels)
    imports = build.fix_imports(events.OUTS)
    print(f"{changed} changed footsteps; {removed} obsolete variants removed; {len(events.OUTS)} footstep files checked; {imports} import options changed")


if __name__ == "__main__":
    main()
