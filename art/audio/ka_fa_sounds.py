"""The KA-FA 1500's sound events (docs/MINICOPTER.md §8, M17): its rotor as loops at two speeds and under load, the
twin's start and its cut. Made in code (rotorcraft.py); events.everything() includes them, and sounds.json's
items["ka-fa-1500"] names their roles for ShipSounds.

    python -I art/audio/build.py --only kafa
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import events as e  # noqa: E402
import rotorcraft as r  # noqa: E402
from build import Out, Seg  # noqa: E402

LOOP_S, LOOP_PRE = 12.0, 3.0
ROLES = {"rotor": "kafa.rotor.slow", "rotorFast": "kafa.rotor.fast", "rotorLoad": "kafa.rotor.load", "start": "kafa.twin.start",
         "shutdown": "kafa.twin.stop", "touchdown": "ship.touchdown", "crash": "ship.crash", "destroyed": "explosion"}


def recipes():
    loop = (LOOP_S, 0.0, LOOP_PRE)
    # The rotor at about half its working speed (spooling up, winding down), at its working speed, and its heavy load (the
    # collective's slap): ShipSounds crossfades the first two by the spool and lays the third on by the collective.
    for key, speed, load in (("slow", 0.5, 0.15), ("fast", 1.0, 0.25), ("load", 1.0, 1.0)):
        e.event("kafa.rotor." + key, [Out("kafa/rotor_%s.ogg" % key, "engine", [
            Seg(r.rotor_loop("kafa_rotor_" + key, speed=speed, load=load, seconds=LOOP_S, pre=LOOP_PRE, seed=7))],
            fx="highpass=f=25", loop=loop)],
            "SFX", 0.0, unitSize=12.0, maxDistance=700.0, priority=72, limit=6, pitch=[1.0, 1.0], jitter=0.0)
    e.event("kafa.twin.start", [Out(e.numbered("kafa/twin_start", k), "machine", [
        Seg(r.twin_start("kafa_twin_start_%d" % k, seed=11 + k, catch=(0.55, 0.62, 0.5)[k]))],
        fx="highpass=f=30", tail_db=-50, max_fade=0.4) for k in range(3)],
        "SFX", 0.0, unitSize=8.0, maxDistance=250.0, priority=70, limit=2, pitch=[0.97, 1.03], jitter=0.5)
    e.event("kafa.twin.stop", [Out(e.numbered("kafa/twin_stop", k), "machine", [
        Seg(r.twin_stop("kafa_twin_stop_%d" % k, seed=21 + k))],
        fx="highpass=f=30", tail_db=-50, max_fade=0.4) for k in range(3)],
        "SFX", -2.0, unitSize=8.0, maxDistance=250.0, priority=70, limit=2, pitch=[0.97, 1.03], jitter=0.5)
