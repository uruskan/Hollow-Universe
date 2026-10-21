"""The KA-FA 1500's sounds, made in code (docs/MINICOPTER.md §8, M17): a two-blade rotor, its tail rotor and the flat
twin that turns them.

No CC0 recording on this PC is a light helicopter, so it is modelled the way one sounds:
- the main rotor's blades slap the air once each per pass (two a turn): a short broadband crack with a low thump under
  it, sharper with the collective (blade loading), over the rotor's whoosh, which swells and falls with each pass;
- the tail rotor turns about 5.4 times as fast and buzzes: its blade-pass and harmonics, a little rough;
- the twin fires once a turn (an even-firing four-stroke twin) at about six turns of the rotor's: its orders, each
  firing's exhaust pop through the muffler, its tappets' tick.
The rotor's speed sets every rate together (they're geared), so a loop at a lower speed is the same machine slower.

Loops are periodic, as machines.py's are (every tone completes whole cycles in the loop and every random source repeats
with it). Cached like machines.py's generators, the code's version in each call's arguments.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import machines as m  # noqa: E402

VERSION = 1
TAU = m.TAU
ROTOR_HZ = 8.0          # the main rotor's turns a second at full speed (480 a minute: ships.json rotors)
TAIL_RATIO = 5.4        # the tail rotor's turns per main rotor turn (2,600 a minute)
TWIN_RATIO = 6.0        # the crankshaft's turns per main rotor turn (2,880 a minute at full speed)


def _frac(x):
    return x - math.floor(x)


def _rotor_parts(n, speed, load, seed, period, start):
    """The rotor, the tail and the twin at a rotor speed (1: working speed) and blade loading (0 … 1)."""
    rate = ROTOR_HZ * speed
    j = m.wander(n, seed + 1, 0.5, period)
    f = [rate * (1.0 + 0.004 * v) for v in j]
    ph = m.phase(f, period, start)                    # main rotor turns (cycles)
    # Blade slap: a decaying crack at each of the two passes a turn, its brightness and weight by the loading.
    crack_ms = 18.0 - 6.0 * load
    pulse = [math.exp(-_frac(2.0 * p) / (crack_ms * 0.001 * 2.0 * rate)) for p in ph]
    hard = m.chain(m.white(n, seed + 2, period), ("bp", 700.0 + 500.0 * load, 0.8), ("hp", 120.0))
    slap = m.mul(hard, [v ** (2.0 - load) for v in pulse])
    thump_ph = [2.0 * p for p in ph]
    thump = m.mul(m.tones(thump_ph, ((1, 1.0, 0.0), (2, 0.35, 0.1))), [v ** 0.5 for v in pulse])
    thump = m.lowpass1(thump, 160.0)
    # The rotor's whoosh: low turbulent air swelling at each pass.
    air = m.chain(m.white(n, seed + 3, period), ("lp", 420.0 + 300.0 * speed, 0.7), ("hp", 30.0))
    swell = [0.55 + 0.45 * math.cos(TAU * 2.0 * p) for p in ph]
    whoosh = m.mul(air, swell)
    # The tail rotor: its blade-pass buzz and harmonics, roughened.
    tail_ph = [TAIL_RATIO * p for p in ph]
    tail = m.tones(tail_ph, ((2, 1.0, 0.0), (4, 0.5, 0.2), (6, 0.3, 0.45), (8, 0.15, 0.7), (1, 0.2, 0.3)))
    tail = m.add(tail, m.scale(m.chain(m.white(n, seed + 4, period), ("bp", 2.0 * TAIL_RATIO * rate * 4.0, 2.0)), 0.3))
    # The twin: its firing once a turn, its exhaust pops, its tappets.
    crank = [TWIN_RATIO * p for p in ph]
    twin = m.tones(crank, ((1, 1.0, 0.0), (2, 0.6, 0.2), (3, 0.3, 0.45), (4, 0.18, 0.1), (6, 0.08, 0.6)))
    fire = [math.exp(-_frac(c) * 9.0) for c in crank]
    pops = m.mul(m.chain(m.white(n, seed + 5, period), ("bp", 380.0, 1.1), ("lp", 2400.0)), fire)
    ticks = m.mul(m.chain(m.white(n, seed + 6, period), ("hp", 3500.0)), [math.exp(-_frac(2.0 * c) * 40.0) for c in crank])
    twin = m.add(m.scale(twin, 0.5), m.scale(pops, 0.9 + 0.8 * load), m.scale(ticks, 0.08))
    twin = m.soft(twin, 1.5 + load)
    return slap, thump, whoosh, tail, twin


@m.made
def rotor_loop(speed, load, seconds=12.0, pre=3.0, seed=1, version=VERSION):
    """The whole machine running at a rotor speed and blade loading (a loop)."""
    n = m.ns(pre + seconds) + 64
    period, start = m.ns(seconds), m.ns(pre)
    slap, thump, whoosh, tail, twin = _rotor_parts(n, speed, load, seed, period, start)
    out = m.add(m.scale(slap, 0.55 * (0.6 + 0.8 * load)), m.scale(thump, 0.45 * speed), m.scale(whoosh, 0.6),
                m.scale(tail, 0.05 * speed), m.scale(twin, 0.16))
    return [out]


@m.made
def twin_start(seconds=3.4, seed=1, catch=0.55, version=VERSION):
    """The twin's start: the recoil starter's pull (a ratcheting whirr), two coughs, it catches and races, then settles
    to the rotor's early speed (the loops take over as the rotor spools)."""
    n = m.ns(seconds)
    rnd = m.Rand(seed)
    out = m.zeros(n)
    # The pull: a rope and pawl whirring up and down.
    pull = m.chain(m.white(n, seed + 1), ("bp", 1400.0, 1.4))
    pull = m.mul(pull, m.env(n, [(0.0, 0.0), (0.05, 0.7), (0.32, 0.9), (0.45, 0.0)]))
    pawl = [0.5 + 0.5 * math.cos(TAU * (35.0 * t / m.RATE + 20.0 * (t / m.RATE) ** 2)) for t in range(n)]
    out = m.add(out, m.scale(m.mul(pull, pawl), 0.25))
    # Two coughs, then it catches.
    for at, amp in ((0.42, 0.8), (catch - 0.03, 0.6)):
        out = m.add(out, m.burst(n, m.ns(at), seed + 2 + int(at * 100), 0.002, 0.01, 0.08, 150.0, 2500.0, amp, 0.4))
    # Running: the crank's rate rising from catch to a race and back down to the rotor's slow speed.
    rate = m.env(n, [(0.0, 0.0), (catch, 0.0), (catch + 0.01, 18.0), (catch + 0.5, 58.0), (catch + 1.2, 34.0), (seconds, 26.0)])
    ph = m.phase(rate)
    fire = [math.exp(-_frac(p) * 9.0) if r > 0 else 0.0 for p, r in zip(ph, rate)]
    pops = m.mul(m.chain(m.white(n, seed + 4), ("bp", 380.0, 1.1), ("lp", 2400.0)), fire)
    body = m.mul(m.tones(ph, ((1, 1.0, 0.0), (2, 0.5, 0.2), (3, 0.25, 0.4))), [1.0 if r > 0 else 0.0 for r in rate])
    run = m.soft(m.add(m.scale(body, 0.5), pops), 2.0)
    run = m.mul(run, m.env(n, [(0.0, 0.0), (catch, 0.0), (catch + 0.05, 1.0), (seconds - 0.4, 0.9), (seconds, 0.0)]))
    out = m.add(out, m.scale(run, 0.55 + 0.1 * rnd.uni()))
    return [out]


@m.made
def twin_stop(seconds=2.2, seed=1, version=VERSION):
    """The twin cut: a few last firings slowing, a backfire through the muffler, the crank's run-down clatter."""
    n = m.ns(seconds)
    rate = m.env(n, [(0.0, 34.0), (0.5, 18.0), (1.1, 6.0), (1.4, 0.0), (seconds, 0.0)])
    ph = m.phase(rate)
    fire = [math.exp(-_frac(p) * 7.0) if r > 1.0 else 0.0 for p, r in zip(ph, rate)]
    pops = m.mul(m.chain(m.white(n, seed + 1), ("bp", 330.0, 1.1), ("lp", 2200.0)), fire)
    out = m.mul(m.soft(pops, 2.0), m.env(n, [(0.0, 1.0), (1.2, 0.7), (1.45, 0.0), (seconds, 0.0)]))
    out = m.add(out, m.burst(n, m.ns(0.95), seed + 2, 0.001, 0.01, 0.12, 90.0, 3000.0, 0.9, 0.5))
    return [m.scale(out, 0.6)]
