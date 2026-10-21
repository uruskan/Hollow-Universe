"""Dedicated injector, hand-held launcher and recycler foley from the existing verified CC0 library.

    python -I art/audio/repair_actions.py

Only these outputs are built; the normal events.everything() also includes these recipes.
The archive originals stay unchanged. Levels and hashes use the same measured build path as all other audio.
"""
import os
import sys
import inspect  # Load stdlib before this folder's legacy inspect.py is on sys.path.

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build
import events as e
import machines as m
from build import Out, Seg
from sources import bsb


def recipes():
    # An automatic injector: cap/catch, the short spring-driven plunger, then its release click.
    e.event("survival.inject", [Out(e.numbered("survival/inject", k), "foley", [
        Seg(e.rpg("metalLatch.ogg"), gain=-4, pitch=1.7 + k * .08, fx="highpass=f=550,lowpass=f=6000"),
        Seg(e.rpg(("cloth1.ogg", "cloth2.ogg", "cloth3.ogg")[k]), gain=-9, at=.14, pitch=1.15),
        Seg(e.kenney("impactMetal_light_%03d.ogg" % k), gain=-7, at=.45, pitch=1.8, fx="highpass=f=1000,lowpass=f=5500"),
        Seg(e.rpg("metalLatch.ogg"), gain=-9, at=.70, pitch=2.1, fx="highpass=f=700,lowpass=f=5000")],
        fx="highpass=f=150", length=1.05, max_fade=.2) for k in range(3)],
        "SFX", -1, maxDistance=12, priority=60)

    # The launch tube's low gas report/backblast is distinct from the rocket's travelling motor and rifle shots.
    e.event("launcher.shot", [Out(e.numbered("weapon/launcher_shot", k), "gunshot", [
        Seg(m.rocket_ignite("hand_launcher_%d" % k, seconds=.8, seed=1941+k, size=.65 + .08*k), gain=-3),
        Seg(bsb(("1340", "1341", "1344")[k]), 0, .8, gain=-5, pitch=1.15, fade_out=.3, fx="highpass=f=80,lowpass=f=6500"),
        Seg(e.kenney("impactMetal_heavy_%03d.ogg" % k), gain=-13, pitch=.72, fx="highpass=f=70,lowpass=f=1700")],
        fx="highpass=f=40", max_fade=.4) for k in range(3)],
        "SFX", 2, unitSize=10, maxDistance=450, priority=90, delay=True)
    for role, at, pitch in (("open", .08, .7), ("load", .15, .85), ("lock", .04, 1.1)):
        e.event("launcher." + role, [Out(e.numbered("weapon/launcher_" + role, k), "mechanism", [
            Seg(e.rubber("metal_open_01.ogg"), gain=-5, pitch=pitch + k*.04, fx="highpass=f=90,lowpass=f=4500"),
            Seg(e.kenney("impactMetal_light_%03d.ogg" % k), gain=-3, at=at, pitch=pitch, fx="highpass=f=120"),
            Seg(e.rpg("clothBelt.ogg"), gain=-11, at=.04, pitch=1.0)], max_fade=.2) for k in range(3)],
            "SFX", 0, maxDistance=22)

    # A loaded gearbox with irregular recorded steel scraping. Separate start/stop mechanisms; no fire crackle.
    e.event("recycler.loop", [Out("deploy/recycler_loop.ogg", "engine", [
        Seg(m.motor_loop("recycler_motor", rot_hz=27, seconds=6, pre=2, seed=1981, gear=17, brush=.8, ring_rumble=1.4, ticks=2.4), gain=-3),
        Seg(m.looped(bsb("0683"), .3, 6, 1, 8.01, pitch=.8), gain=-10, fx="highpass=f=170,lowpass=f=5200")],
        fx="highpass=f=50", loop=(6, 0, 2))], "SFX", 0, unitSize=3, maxDistance=45, priority=40, limit=6, pitch=[1, 1], jitter=0)
    for role, reverse in (("start", False), ("stop", True)):
        e.event("recycler." + role, [Out(e.numbered("deploy/recycler_" + role, k), "machine", [
            Seg(bsb("0304"), 0, .7, gain=-6, pitch=.72+k*.04, reverse=reverse, fade_out=.25),
            Seg(e.kenney("impactMetal_heavy_%03d.ogg" % k), gain=-5, at=.03 if not reverse else .55, pitch=.8),
            Seg(e.rpg("metalLatch.ogg"), gain=-9, at=.02, pitch=.8)],
            fx="highpass=f=70,lowpass=f=6500", length=1.1, max_fade=.3) for k in range(3)],
            "SFX", 0, maxDistance=45, priority=55)


if __name__ == "__main__":
    recipes()
    levels = build.load_json(build.LEVELS, {"files": {}})["files"]
    for output in e.OUTS:
        measure = build.build(output)
        levels[output.rel] = measure
        print("%s: %.1f LUFS, %.2f dBTP" % (output.rel, build.level_of(measure, build.CATEGORIES[output.category][0]), measure["truePeak"]))
    build.write_levels(levels)
    old = build.load_json(build.CATALOGUE, {}).get("events", [])
    replacements = {event["key"]: event for event in e.EVENTS}
    ordered = [replacements.pop(event["key"], event) for event in old]
    build.write_catalogue(ordered + list(replacements.values()), e.ITEMS, e.SPACES)
    build.fix_imports(e.OUTS)
