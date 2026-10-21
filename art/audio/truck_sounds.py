"""The TOGG's recorded diesel (docs/VEHICLES.md §9.10; the owner, 21 October 2026: "as if this was a trucking game",
recordings over synthesis): loops by revs on and off load cut from real heavy diesels, the turbo's whistle with a real
twin-turbo's whine under it, the turbo's flutter when it lets go, gear changes, air brakes, a Jake brake, the start and
the stop; and the Monster TOGG's own start and lumpy idle, a V12's cold start. Every source is in sources.py (freesound,
Pixabay, YouTube, checked by SHA-256) and credited in game/assets/THIRD_PARTY.md. vehicle_sounds.py names them in the
TOGGs' `engines` entries.

Where each loop comes from (looked at as spectrograms, art/.cache: the firing order's line gives the revs, rpm = 20 × Hz
for a four-stroke six):
- idle, 600 rpm: a 1980s Ford LTL 9000 Class-8 tractor idling (freesound 435656), its firing at 29 Hz, from 6.5 s;
- 1,700 rpm on load: a big diesel truck working up a hill (freesound 637914), dead steady at 85 Hz; the same pitched
  down to 1,200 rpm; off load at 1,700, a semi cruising before its Jake brake comes in (Pixabay 63220, 85 Hz);
- 2,400 rpm: a Fendt tractor driving, from its cab (freesound 518294), steady at 95 Hz from 10 s, pitched up;
- off load (the overrun, fuel cut): each on-load loop with its combustion's bark filtered away;
- the overboost's layer: a twin-turbo Power Stroke pulling tractor at full song (YouTube DoC1_kqCoh0, 11.6–19 s);
- the turbo: the synthesized whistle (engines.py) with that tractor's real compressor whine (4–7 kHz) under it.

    python -I art/audio/build.py --only vehicle/truck
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import engines as g  # noqa: E402
import events as e  # noqa: E402
import sources  # noqa: E402
from build import Out, Seg  # noqa: E402

LOOP, OVERLAP = 6.0, 1.5
T = "vehicle/truck/"

# The loops: revs, (on-load source, start s, its own revs), off-load source (or None: the on-load one filtered), the
# loops' levels on and off load against the others. A source is pitched (varispeed) from its own revs to the loop's.
FS, PX, YT = sources.freesound, sources.pixabay, sources.youtube
TRUCK = [
    (600, (FS, "435656", 6.5, 600.0), None, -4.0, -4.0),
    (1200, (FS, "637914", 1.0, 1700.0), None, -3.0, -9.0),
    (1700, (FS, "637914", 2.0, 1700.0), (PX, "63220", 0.6, 1700.0), -1.5, -7.0),
    (2400, (FS, "518294", 11.0, 1900.0), (FS, "518294", 22.0, 1900.0), 0.0, -6.0),
]

# The overrun: no combustion, so no bark; the intake's and the exhaust's air and the gears' whine.
OFF_FX = "lowpass=f=650,highpass=f=28,equalizer=f=120:t=q:w=1:g=-4"

# The overboost's layer: the twin-turbo tractor's revs (a guess from its spectrogram: the game pitches it from here).
LABOUR_RPM = 1900.0


def _key(rpm, load):
    return "veh.truck.idle" if rpm == TRUCK[0][0] else "veh.truck.%s.%d" % (load, rpm)


def loops():
    """sounds.json `loops` for the recorded six."""
    out = []
    for rpm, _, _, on_db, off_db in TRUCK:
        out.append({"rpm": rpm, "on": _key(rpm, "on"), "off": _key(rpm, "off"), "onDb": on_db, "offDb": off_db})
    return out


def _loop_seg(src, rpm, fx=""):
    fetch, sid, start, own = src
    pitch = rpm / own
    # Enough of the source for the loop and its overlap once pitched.
    return Seg(fetch(sid), start, (LOOP + OVERLAP) * pitch + 0.05, pitch=pitch, fx=fx)


def _loop(key, rel, segs, reach, priority=68, volume=0.0, fx="highpass=f=24"):
    e.event(key, [Out(T + rel, "engine", segs, fx=fx, loop=(LOOP, OVERLAP), fade_in=0.0)], "SFX", volume, unitSize=9.0,
            maxDistance=reach, priority=priority, limit=6, pitch=[1.0, 1.0], jitter=0.0)


def _shots(key, rels, segs, category, volume, unit, reach, priority=60, limit=3, pitch=(0.97, 1.03), jitter=1.0,
           fx="highpass=f=30"):
    outs = [Out(T + r, category, [s], fx=fx, tail_db=-50, max_fade=0.5) for r, s in zip(rels, segs)]
    e.event(key, outs, "SFX", volume, unitSize=unit, maxDistance=reach, priority=priority, limit=limit, pitch=list(pitch),
            jitter=jitter)


def recipes():
    # The loops by revs, on load and off it (the idle one loop both ways).
    for rpm, on, off, _, _ in TRUCK:
        if rpm == TRUCK[0][0]:
            _loop("veh.truck.idle", "idle.ogg", [_loop_seg(on, rpm)], 260.0)
            continue
        _loop(_key(rpm, "on"), "on_%d.ogg" % rpm, [_loop_seg(on, rpm)], 340.0)
        _loop(_key(rpm, "off"), "off_%d.ogg" % rpm, [_loop_seg(off or on, rpm, OFF_FX if off is None else "")], 220.0)

    # Overboosted: the twin-turbo tractor at full song, a layer the game swells by the overboost and the load.
    _loop("veh.truck.labour", "labour.ogg", [Seg(YT("DoC1_kqCoh0"), 11.6, LOOP + OVERLAP + 0.05)], 360.0, 70)

    # The turbo: the synthesized whistle (periodic, from its pre-roll on) with the real tractor's compressor whine.
    whistle = g.turbo_whistle("veh_truck_turbo", seconds=LOOP + OVERLAP, pre=1.0, seed=9)
    _loop("veh.truck.turbo", "turbo.ogg",
          [Seg(whistle, 1.0, LOOP + OVERLAP, gain=-3.0),
           Seg(YT("DoC1_kqCoh0"), 11.6, LOOP + OVERLAP, fx="highpass=f=3600,lowpass=f=7600", gain=0.0)],
          140.0, 56, -6.0, fx="highpass=f=900")

    # Letting go at boost: a diesel's turbo flutter (a stock diesel has no blow-off valve).
    _shots("veh.truck.flutter", ["flutter_0.ogg", "flutter_1.ogg"],
           [Seg(PX("345714"), 0.05, 2.2, fade_out=0.8), Seg(PX("336362"), 0.05, 2.2, fade_out=0.8)],
           "machine", -6.0, 5.0, 110.0, 56, 2)

    # A gear change: a fifth wheel's and a gearbox's heavy metal clunks, and a 1929 truck's gear lever.
    trailer, lever = FS("165167"), FS("107039")
    _shots("veh.truck.shift", ["shift_0.ogg", "shift_1.ogg", "shift_2.ogg", "shift_3.ogg"],
           [Seg(trailer, 3.78, 0.6, fade_out=0.25), Seg(trailer, 7.58, 0.6, fade_out=0.25),
            Seg(lever, 6.85, 0.6, fade_out=0.25), Seg(lever, 9.77, 0.6, fade_out=0.25)],
           "mechanism", -3.0, 4.0, 60.0, 50, 2, fx="highpass=f=60")

    # Air brakes as it comes to rest: a truck's release, and a tractor's as it couples its trailer.
    _shots("veh.truck.airbrake", ["airbrake_0.ogg", "airbrake_1.ogg"],
           [Seg(FS("504865"), 0.0, 5.5, fade_out=1.5), Seg(trailer, 10.35, 1.9, fade_out=0.6)],
           "machine", -4.0, 6.0, 140.0, 58, 2, fx="highpass=f=180")

    # Hard on the brakes at speed: a semi's Jake brake (the engine's own brake) barking as it slows.
    _shots("veh.truck.jake", ["jake.ogg"], [Seg(PX("63220"), 9.4, 6.6, fade_in=0.05, fade_out=1.2)],
           "machine", -2.0, 9.0, 320.0, 64, 1, (0.98, 1.02), 0.3)

    # The start (cranked, caught, settling) and the stop: one Ford LTL 9000.
    _shots("veh.truck.start", ["start.ogg"], [Seg(FS("435656"), 0.8, 6.7, fade_out=0.8)], "machine", 0.0, 9.0, 260.0, 68, 1,
           (0.99, 1.01), 0.3)
    _shots("veh.truck.stop", ["stop.ogg"], [Seg(FS("435667"), 80.5, 6.5, fade_out=0.6)], "machine", -2.0, 9.0, 200.0, 68, 1,
           (0.99, 1.01), 0.3)

    # The Monster TOGG: a Peterbilt's V12 cold start (cranking, then running), and its lumpy, hunting idle.
    v12 = YT("rzCpNBSp7YY")
    e.event("veh.truck.v12.start", [Out(T + "v12_start.ogg", "machine",
                                        [Seg(v12, 33.4, 4.0, fade_out=0.08),
                                         Seg(v12, 38.6, 6.0, at=3.92, fade_in=0.08, fade_out=1.0)],
                                        fx="highpass=f=25", tail_db=-50, max_fade=0.5)],
            "SFX", 1.0, unitSize=10.0, maxDistance=300.0, priority=68, limit=1, pitch=[0.99, 1.01], jitter=0.3)
    _loop("veh.truck.v12.idle", "v12_idle.ogg", [Seg(v12, 70.0, LOOP + OVERLAP + 0.05)], 300.0)
