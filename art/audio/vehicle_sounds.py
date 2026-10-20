"""Ground vehicles' sound events (docs/VEHICLES.md §7.1): the Zamazingo's single and the TOGG's turbo-diesel six as loops
by revs, on the throttle and off it; their starts, stops and gear changes; the turbo; tyres on each ground, skids; the
suspension's knocks, creaks and landings; a splash. Made in code (engines.py) from the CC0 recordings already used for
footsteps. events.everything() includes them; sounds.json's `engines` (ENGINES here) gives each ground vehicle kind its
revs, loops, tyres and suspension for VehicleRevs and ShipSounds, and `items` its start, stop and landing roles.

    python -I art/audio/build.py --only vehicle
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import engines as g  # noqa: E402
import events as e  # noqa: E402
import sources  # noqa: E402
from build import Out, Seg  # noqa: E402

LOOP_S, LOOP_PRE = 6.0, 1.0
TYRE_S = 8.0
SURFACES = ("gravel", "dirt", "grass", "sand", "snow", "rock", "water")

# The revs each engine's loops are made at (the first is its idle, on and off alike), with each loop's level on the
# throttle and off it against the rest: louder with the revs, the overrun well under the pull.
SINGLE = [(1450, -7.0, -7.0), (2400, -4.5, -10.0), (3600, -3.0, -9.0), (5000, -1.5, -8.0), (6600, -0.5, -7.0),
          (8600, 0.5, -6.0)]
DIESEL = [(750, -4.0, -4.0), (1050, -3.5, -9.0), (1450, -2.5, -8.0), (1950, -1.5, -7.0), (2500, -0.5, -6.0),
          (3000, 0.5, -5.0)]


def _loops(name, table):
    out = []
    for i, (rpm, on_db, off_db) in enumerate(table):
        if i == 0:
            key = "veh.%s.idle" % name
            out.append({"rpm": rpm, "on": key, "off": key, "onDb": on_db, "offDb": off_db})
        else:
            out.append({"rpm": rpm, "on": "veh.%s.on.%d" % (name, rpm), "off": "veh.%s.off.%d" % (name, rpm),
                        "onDb": on_db, "offDb": off_db})
    return out


TYRES = dict({s: "veh.tyre." + s for s in SURFACES}, hard="veh.tyre.rock")

ENGINES = {
    # The Zamazingo: a 400 cc single, light flywheel, a quick five-speed box; 25 m/s at the redline in fifth.
    "zamazingo": {
        "idle": 1450, "redline": 8800, "limiter": 9100, "gears": [7.0, 11.0, 15.0, 19.5, 25.6], "reverse": 4.0,
        "response": 0.06, "upshift": 0.86, "downshift": 0.42, "shiftTime": 0.12, "launch": 0.42,
        "pitch": 1.0, "volume": 0.0, "loops": _loops("single", SINGLE), "shift": "veh.single.shift",
        "tyres": TYRES, "tyreSpeed": 10.0, "tyrePitch": 1.15, "tyreDb": -7.0,
        "skidLoose": "veh.tyre.skid.loose", "skidHard": "veh.tyre.skid.hard",
        "knock": "veh.susp.knock.light", "creak": "veh.susp.creak.light", "land": "veh.susp.land.light",
        "splash": "veh.splash"},
    # The TOGG: a 6.7 l turbo-diesel six, a heavy flywheel, a slow five-speed truck box; 20 m/s at 3,000 rpm in fifth.
    "togg": {
        "idle": 750, "redline": 3000, "limiter": 3100, "gears": [4.2, 7.0, 10.5, 15.0, 20.6], "reverse": 5.0,
        "response": 0.22, "upshift": 0.85, "downshift": 0.45, "shiftTime": 0.45, "launch": 0.42, "turboSpool": 0.8,
        "pitch": 1.0, "volume": 0.0, "loops": _loops("diesel", DIESEL), "turbo": "veh.diesel.turbo",
        "blowoff": "veh.diesel.release", "shift": "veh.diesel.shift",
        "tyres": TYRES, "tyreSpeed": 10.0, "tyrePitch": 0.8, "tyreDb": -3.0,
        "skidLoose": "veh.tyre.skid.loose", "skidHard": "veh.tyre.skid.hard",
        "knock": "veh.susp.knock.heavy", "creak": "veh.susp.creak.heavy", "land": "veh.susp.land.heavy",
        "splash": "veh.splash"},
}
# The monster TOGG (the vehicles session's kind): the TOGG's six, lower and louder, on bigger tyres.
ENGINES["monster-togg"] = dict(ENGINES["togg"], pitch=0.86, volume=3.0, tyrePitch=0.7, tyreDb=-1.5)

# sounds.json `items` roles (ShipSounds: the start and stop, a crash, the wreck). No `touchdown`: a vehicle's wheels leave
# and meet the ground all the time; VehicleSounds lands it only after a jump, by how fast it fell.
ROLES = {
    "zamazingo": {"start": "veh.single.start", "shutdown": "veh.single.stop", "crash": "ship.crash", "destroyed": "explosion"},
    "togg": {"start": "veh.diesel.start", "shutdown": "veh.diesel.stop", "crash": "ship.crash", "destroyed": "explosion"},
}
ROLES["monster-togg"] = dict(ROLES["togg"])

V = "vehicle/"


def _loop_event(key, rel, path, unit, reach, priority=66, volume=0.0):
    e.event(key, [Out(V + rel, "engine", [Seg(path)], fx="highpass=f=22", loop=(LOOP_S, 0.0, LOOP_PRE))],
            "SFX", volume, unitSize=unit, maxDistance=reach, priority=priority, limit=6, pitch=[1.0, 1.0], jitter=0.0)


def _shots(key, rels, paths, category, volume, unit, reach, priority=60, limit=3, pitch=(0.96, 1.04), jitter=1.0, fx="highpass=f=25"):
    outs = [Out(V + r, category, [Seg(p)], fx=fx, tail_db=-50, max_fade=0.4) for r, p in zip(rels, paths)]
    e.event(key, outs, "SFX", volume, unitSize=unit, maxDistance=reach, priority=priority, limit=limit,
            pitch=list(pitch), jitter=jitter)


def recipes():
    # The single: idle, then on and off the throttle at each speed.
    for i, (rpm, _, _) in enumerate(SINGLE):
        if i == 0:
            _loop_event("veh.single.idle", "single_idle.ogg",
                        g.single_loop("veh_single_idle", rpm=float(rpm), load=0.0, over=0.0, seconds=LOOP_S, pre=LOOP_PRE, seed=5),
                        6.0, 220.0)
            continue
        _loop_event("veh.single.on.%d" % rpm, "single_on_%d.ogg" % rpm,
                    g.single_loop("veh_single_on_%d" % rpm, rpm=float(rpm), load=1.0, over=0.0, seconds=LOOP_S, pre=LOOP_PRE, seed=5),
                    6.0, 260.0)
        _loop_event("veh.single.off.%d" % rpm, "single_off_%d.ogg" % rpm,
                    g.single_loop("veh_single_off_%d" % rpm, rpm=float(rpm), load=0.0, over=1.0, seconds=LOOP_S, pre=LOOP_PRE, seed=5),
                    6.0, 200.0)
    _shots("veh.single.start", [e.numbered("single_start", k) for k in range(3)],
           [g.single_start("veh_single_start_%d" % k, seed=31 + k, kicks=(1, 2, 2)[k]) for k in range(3)],
           "machine", -2.0, 6.0, 180.0, 66, 2, (0.98, 1.02), 0.5)
    _shots("veh.single.stop", [e.numbered("single_stop", k) for k in range(2)],
           [g.single_stop("veh_single_stop_%d" % k, seed=41 + k) for k in range(2)], "machine", -4.0, 6.0, 150.0, 66, 2,
           (0.98, 1.02), 0.5)
    _shots("veh.single.shift", [e.numbered("single_shift", k) for k in range(4)],
           [g.gear_clunk("veh_single_shift_%d" % k, seed=51 + k, heavy=0.0) for k in range(4)], "mechanism", -6.0, 3.0, 40.0, 50, 2)

    # The diesel six: idle, on and off the throttle (its fuel cut), the turbo, its release.
    for i, (rpm, _, _) in enumerate(DIESEL):
        if i == 0:
            _loop_event("veh.diesel.idle", "diesel_idle.ogg",
                        g.diesel_loop("veh_diesel_idle", rpm=float(rpm), load=0.0, fuel=1.0, seconds=LOOP_S, pre=LOOP_PRE, seed=7),
                        9.0, 260.0, 68)
            continue
        _loop_event("veh.diesel.on.%d" % rpm, "diesel_on_%d.ogg" % rpm,
                    g.diesel_loop("veh_diesel_on_%d" % rpm, rpm=float(rpm), load=1.0, fuel=1.0, seconds=LOOP_S, pre=LOOP_PRE, seed=7),
                    9.0, 340.0, 68)
        _loop_event("veh.diesel.off.%d" % rpm, "diesel_off_%d.ogg" % rpm,
                    g.diesel_loop("veh_diesel_off_%d" % rpm, rpm=float(rpm), load=0.0, fuel=0.0, seconds=LOOP_S, pre=LOOP_PRE, seed=7),
                    9.0, 220.0, 68)
    _loop_event("veh.diesel.turbo", "diesel_turbo.ogg", g.turbo_whistle("veh_diesel_turbo", seconds=LOOP_S, pre=LOOP_PRE, seed=9),
                5.0, 120.0, 55, -6.0)
    _shots("veh.diesel.release", [e.numbered("diesel_release", k) for k in range(3)],
           [g.turbo_release("veh_diesel_release_%d" % k, seed=61 + k) for k in range(3)], "machine", -8.0, 5.0, 100.0, 55, 2)
    _shots("veh.diesel.start", [e.numbered("diesel_start", k) for k in range(2)],
           [g.diesel_start("veh_diesel_start_%d" % k, seed=71 + k, catch=(1.25, 1.05)[k]) for k in range(2)],
           "machine", 0.0, 9.0, 260.0, 68, 2, (0.98, 1.02), 0.5)
    _shots("veh.diesel.stop", [e.numbered("diesel_stop", k) for k in range(2)],
           [g.diesel_stop("veh_diesel_stop_%d" % k, seed=81 + k) for k in range(2)], "machine", -3.0, 9.0, 200.0, 68, 2,
           (0.98, 1.02), 0.5)
    _shots("veh.diesel.shift", [e.numbered("diesel_shift", k) for k in range(4)],
           [g.gear_clunk("veh_diesel_shift_%d" % k, seed=91 + k, heavy=1.0) for k in range(4)], "mechanism", -4.0, 4.0, 50.0, 50, 2)

    # Tyres on each ground at 10 m/s (the game pitches and swells them by speed), and a skid on loose ground and hard.
    for s in SURFACES:
        e.event("veh.tyre." + s, [Out(V + "tyre_%s.ogg" % s, "engine",
                                      [Seg(g.tyre_loop("veh_tyre_" + s, surface=s, seconds=TYRE_S, seed=101))],
                                      fx="highpass=f=30", loop=(TYRE_S, 0.0, 0.0))],
                "SFX", 0.0, unitSize=4.0, maxDistance=90.0, priority=48, limit=6, pitch=[1.0, 1.0], jitter=0.0)
    for name, loose in (("loose", 1), ("hard", 0)):
        e.event("veh.tyre.skid." + name, [Out(V + "skid_%s.ogg" % name, "engine",
                                              [Seg(g.skid_loop("veh_skid_" + name, loose=loose, seconds=LOOP_S, seed=111))],
                                              fx="highpass=f=60", loop=(LOOP_S, 0.0, 0.0))],
                "SFX", 0.0, unitSize=5.0, maxDistance=140.0, priority=52, limit=4, pitch=[1.0, 1.0], jitter=0.0)

    # The suspension: knocks, creaks and landings, light (the bike) and heavy (the TOGG); a splash into water.
    for weight, heavy in (("light", 0.0), ("heavy", 1.0)):
        _shots("veh.susp.knock." + weight, [e.numbered("knock_%s" % weight, k) for k in range(4)],
               [g.suspension_knock("veh_knock_%s_%d" % (weight, k), seed=121 + k, heavy=heavy) for k in range(4)],
               "impact", -6.0 + 3.0 * heavy, 4.0, 90.0, 55, 3)
        _shots("veh.susp.creak." + weight, [e.numbered("creak_%s" % weight, k) for k in range(4)],
               [g.suspension_creak("veh_creak_%s_%d" % (weight, k), seed=131 + k, heavy=heavy) for k in range(4)],
               "mechanism", -12.0, 3.0, 35.0, 35, 2)
        _shots("veh.susp.land." + weight, [e.numbered("land_%s" % weight, k) for k in range(3)],
               [g.suspension_land("veh_land_%s_%d" % (weight, k), seed=141 + k, heavy=heavy) for k in range(3)],
               "impact", -2.0 + 2.0 * heavy, 6.0, 140.0, 62, 2)
    splashes = []
    for sid in ("1519", "1520", "1521"):
        src = sources.bsb(sid)
        ev = e.hits(src, 1, max_len=1.6)[0]
        splashes.append(Out(V + "splash_%s.ogg" % sid, "impact", [e.seg_of(src, ev, 0.2)], fx="highpass=f=60", tail_db=-50,
                            max_fade=0.4))
    e.event("veh.splash", splashes, "SFX", -2.0, unitSize=5.0, maxDistance=90.0, priority=55, limit=3, pitch=[0.92, 1.05],
            jitter=1.0)
