"""Rebuild changed footstep recipes through the shared encoder, without enumerating unrelated synthesis.

    python -I art/audio/repair_footsteps.py

The full build.py path uses the identical recipes. Footwear volume/distance follow footsteps.json;
other playback settings and non-footstep catalogue/measurement entries are preserved.
"""
import os
import sys
import inspect  # Load stdlib before the adjacent legacy inspect.py can shadow it.
import concurrent.futures
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build
import events


def catalogue_order(old_events, regenerated):
    """The events in the order a full build writes them. footsteps() is the first group events.everything() registers,
    so its events lead the catalogue in its own order; every other event keeps its place, and a footstep event that
    footsteps() no longer registers is dropped. Appending new ones at the end instead made the next full build move
    them and rewrite the whole file."""
    replaced = {event["key"] for event in regenerated}
    return list(regenerated) + [event for event in old_events
                                if event["key"] not in replaced and not event["key"].startswith("footstep.")]


def main():
    events.footsteps()
    levels = build.load_json(build.LEVELS, {"files": {}})["files"]
    todo = []
    for output in events.OUTS:
        path = os.path.join(build.ASSETS, output.rel)
        prior = levels.get(output.rel)
        if prior and prior.get("recipe") == output.recipe() and os.path.isfile(path) and prior.get("sha256") == build.sha256(path):
            continue
        todo.append(output)

    def render(output):
        measured = build.build(output)
        metric, _, low, high, _ = build.CATEGORIES[output.category]
        if not low <= measured[metric] <= high or measured["truePeak"] > build.TRUE_PEAK_CEILING:
            raise RuntimeError(f"{output.rel}: measured audio outside category limits")
        if measured["duration"] >= 0.08 and measured["tail"] > -30:
            raise RuntimeError(f"{output.rel}: contact tail ends too abruptly ({measured['tail']} dB)")
        return output, measured, metric

    changed = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        for output, measured, metric in pool.map(render, todo):
            levels[output.rel] = measured
            changed += 1
            print(f"{output.rel}: {measured[metric]:.2f} LUFS, {measured['truePeak']:.2f} dBTP", flush=True)
    build.write_levels(levels)
    old = build.load_json(build.CATALOGUE, {})["events"]
    build.write_catalogue(catalogue_order(old, events.EVENTS), events.ITEMS, events.SPACES,
                          replace_prefixes=("footstep.",))
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
