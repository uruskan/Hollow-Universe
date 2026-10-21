"""Ground vehicles' sounds made in code (docs/VEHICLES.md §7.1): the Zamazingo's air-cooled single and the TOGG's
turbo-diesel six as loops by revs and load, their starts, stops and gear changes, a turbo's whistle and release; tyres
rolling on each ground, skids, and the suspension's knocks, creaks and landings.

No CC0 recording on this PC is an engine, so the engines are modelled the way they make their sound:
- each firing is a pressure pulse leaving the port (sharper under load, a suction after it); it rings the header or the
  stacks (a pipe: a delay whose open end reflects it inverted, losing its top at every pass) and is smoothed by the
  silencer, or first by the turbo's turbine;
- combustion also strikes the metal: an air-cooled single's fins ping; a diesel's sudden pressure rise knocks the block
  (the clatter), loudest at idle and light load, its six cylinders each a little different, so the clatter has the
  rhythm of the cycle (two turns) and the exhaust a lope; a stack's rain cap flaps at idle;
- the intake honks under load, tappets tick, timing gears whine, the fan whooshes; a single on the overrun pops and
  crackles; a diesel on the overrun cuts its fuel and only pumps air.
The tyres are grains of the CC0 footstep recordings (NOX's surface banks, BigSoundBank's gravel and water) strewn as a
tread meets the ground, over the tread's own rolling hum and roar.

Loops are periodic, as machines.py's are (every tone completes whole cycles in the loop and every random source repeats
with it; grains wrap round the loop's end). Each engine loop is made at one revs and load; the game pitches it to the revs
of VehicleRevs and crossfades it with its neighbours (ShipSounds). Cached like machines.py's generators, the code's
version in each call's arguments.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import audiolib  # noqa: E402
import machines as m  # noqa: E402
import sources  # noqa: E402

VERSION = 3
RATE = m.RATE
TAU = m.TAU
exp, sin, cos, sqrt = math.exp, math.sin, math.cos, math.sqrt

SINGLE_IDLE, SINGLE_REDLINE = 1450.0, 8800.0
DIESEL_IDLE, DIESEL_REDLINE = 750.0, 3000.0


def _frac(x):
    return x - math.floor(x)


# ---------------------------------------------------------------- pulses, pipes and events

def crossings(ph, per=1, offset=0.0):
    """(sample, count) where per x phase + offset passes a whole number."""
    out = []
    last = math.floor(ph[0] * per + offset)
    for i in range(1, len(ph)):
        k = math.floor(ph[i] * per + offset)
        if k != last:
            out.append((i, k))
            last = k
    return out


def periodic_count(ph, per, period, start):
    """How many events of `per` a cycle fall in one period (0 without a period): random tables repeat with it."""
    if not period or start + period >= len(ph):
        return 0
    return max(1, int(round((ph[start + period] - ph[start]) * per)))


def kernel(tau_ms):
    """A blowdown pulse: a quick rise and fall over tau, x e^(1-x), then the suction after it, a third its size and
    three times as slow (so it carries no DC)."""
    tau = tau_ms * 0.001 * RATE
    n = int(tau * 14) + 8
    return [x * exp(1.0 - x) - (x / 3.0) * exp(1.0 - x / 3.0) / 3.0 for x in (j / tau for j in range(n))]


def place(n, evs, amps, kern):
    """Each event's kernel, scaled by its amplitude, added at its sample."""
    out = m.zeros(n)
    width = len(kern)
    for (i, _), a in zip(evs, amps):
        if a == 0.0:
            continue
        end = min(n, i + width)
        out[i:end] = [o + a * k for o, k in zip(out[i:end], kern)]
    return out


def impulses(n, evs, amps, delay=0):
    """A unit impulse of each amplitude at its event's sample (plus `delay` samples)."""
    out = m.zeros(n)
    for (i, _), a in zip(evs, amps):
        j = i + delay
        if 0 <= j < n:
            out[j] += a
    return out


def pipe(x, delay_s, reflect, loss_hz):
    """A pipe's resonance: a feedback delay of the round trip, reflected by `reflect` (negative at an open end), losing
    its top above `loss_hz` at each pass."""
    d = max(1, int(round(delay_s * RATE)))
    a = exp(-TAU * loss_hz / RATE)
    b = 1.0 - a
    out = m.zeros(len(x))
    lp = 0.0
    for i, v in enumerate(x):
        lp = b * (out[i - d] if i >= d else 0.0) + a * lp
        out[i] = v + reflect * lp
    return out


def table(rng, count, width=4):
    """`count` rows of `width` uniform numbers (4096 when not periodic)."""
    return [[rng.uni() for _ in range(width)] for _ in range(count or 4096)]


def noise_gate(ph, per, open_at, close_at, n):
    """A smooth window (0-1) while the cycle's phase is between open_at and close_at (an intake stroke)."""
    out = m.zeros(n)
    span = close_at - open_at
    for i, p in enumerate(ph):
        f = _frac(p * per) - open_at
        if 0.0 <= f < span:
            out[i] = sin(math.pi * f / span) ** 2
    return out


# ---------------------------------------------------------------- the Zamazingo's single

def single_parts(n, rpm, load, over, seed, period=None, start=0, misfire=0.0):
    """An air-cooled 400 cc four-stroke single at `rpm` (per sample), throttle `load` (0-1) and overrun `over` (0-1):
    one firing every two turns into a 1 m header and a short enduro silencer; its fins' ping, tappets, primary gears,
    intake honk, clutch rattle at idle; on the overrun weak, uneven firings and afterfire pops."""
    rng = m.Rand(seed)
    jit = m.wander(n, seed + 1, 1.6, period)
    span = SINGLE_REDLINE - SINGLE_IDLE
    cycle = [r / 120.0 * (1.0 + (0.003 + 0.012 * max(0.0, 1.0 - (r - SINGLE_IDLE) / (0.4 * span))) * j)
             for r, j in zip(rpm, jit)]
    ph = m.phase(cycle, period, start)
    evs = crossings(ph)
    rows = table(rng, periodic_count(ph, 1, period, start), 5)
    sharp, soft, pops, pop_amps, fins, gas = [], [], [], [], [], []
    for i, k in evs:
        u = rows[k % len(rows)]
        load_i, over_i, r = load[i], over[i], rpm[i]
        idleness = max(0.0, 1.0 - (r - SINGLE_IDLE) / (0.5 * span))
        var = 0.06 + 0.22 * (1.0 - load_i) * idleness + 0.3 * over_i
        a = (0.14 + 0.86 * load_i if over_i > 0.05 else 0.32 + 0.68 * load_i) * (1.0 + var * (2.0 * u[0] - 1.0))
        if u[1] < 0.012 + 0.05 * idleness * (1.0 - load_i) + 0.22 * over_i + misfire:
            a *= 0.08
        sharp.append(a * load_i)
        soft.append(a * (1.0 - load_i))
        gas.append(a)
        fins.append(a * (0.25 + 0.75 * load_i))
        # The overrun's afterfire: unburnt mixture lighting in the hot pipe a moment after the stroke.
        if over_i > 0.05 and u[2] < 0.32 * over_i:
            cyc_samples = RATE / max(cycle[i], 1.0)
            pops.append((i + int((0.25 + 0.35 * u[3]) * cyc_samples), k))
            pop_amps.append(0.7 + 1.1 * u[4])
    # Each pulse sharp under load, softer off it, and the slower push of its gas behind it (the thud); the pops.
    excite = m.add(place(n, evs, sharp, kernel(0.42)), place(n, evs, soft, kernel(0.95)),
                   place(n, evs, [0.5 * a for a in gas], kernel(3.0)), place(n, pops, pop_amps, kernel(0.3)))
    # The gas's own roar in each pulse, and the pops' crackle.
    train = impulses(n, evs, gas)
    gas_noise = m.mul(m.chain(m.white(n, seed + 2, period), ("bp", 900.0, 0.6)), m.decay(train, 4.0))
    crackle = m.mul(m.chain(m.white(n, seed + 3, period), ("hp", 1400.0), ("lp", 7000.0)),
                    m.decay(impulses(n, pops, pop_amps), 9.0))
    header = pipe(m.add(excite, m.mul(gas_noise, [0.04 + 0.1 * l for l in load])), 0.0044, -0.45, 1800.0)
    silenced = m.chain(header, ("peak", 95.0, 1.0, 4.0), ("lp", 1600.0, 0.75), ("hs", 3200.0, 0.7, -6.0))
    bark = m.mul(m.chain(header, ("hp", 1800.0)), [0.08 + 0.2 * l for l in load])
    exhaust = m.add(silenced, bark, m.scale(crackle, 0.035))
    # The fins ping as it fires; four tappet ticks a cycle; the primary gears' whine; the intake's honk under load.
    ping = m.ring(impulses(n, evs, fins, delay=int(0.0004 * RATE)),
                  [(1750.0, 0.025, 1.0), (2650.0, 0.018, 0.7), (3900.0, 0.014, 0.5), (5200.0, 0.01, 0.35)], n)
    ticks = []
    for off in (0.08, 0.31, 0.58, 0.83):
        ticks += crossings(ph, 1, off)
    tick = m.ring(impulses(n, ticks, [1.0] * len(ticks)), [(4300.0, 0.012, 1.0), (6900.0, 0.008, 0.6)], n)
    crank = [2.0 * p for p in ph]
    whine = m.mul(m.tones(crank, ((21, 1.0, 0.0), (42, 0.3, 0.3))), [0.004 * (r / SINGLE_REDLINE) ** 1.5 for r in rpm])
    intake = m.mul(m.chain(m.white(n, seed + 4, period), ("bp", 430.0, 1.3), ("peak", 170.0, 1.2, 6.0)),
                   m.mul(noise_gate(ph, 1, 0.05, 0.32, n), [0.25 * l * (0.3 + 0.7 * r / SINGLE_REDLINE) for l, r in zip(load, rpm)]))
    rattle = m._rattle(n, seed + 5, [max(4.0, c * 1.5) for c in cycle], 0.03, period)
    rattle = m.mul(rattle, [max(0.0, 1.0 - (r - SINGLE_IDLE) / 1500.0) * (1.0 - l) for r, l in zip(rpm, load)])
    thump = m.lowpass1(m.mul(m.tones(ph, ((1, 1.0, 0.0), (2, 0.55, 0.15), (3, 0.25, 0.35))), [0.3 + 0.7 * l for l in load]), 180.0)
    return {"exhaust": exhaust, "ping": m.scale(ping, 0.22), "tick": m.scale(tick, 0.05), "whine": whine,
            "intake": intake, "rattle": rattle, "thump": m.scale(thump, 0.07)}


def warm(x, drive):
    """A little saturation of the whole (its peak brought to 1 first, so the drive means the same at any level)."""
    return m.soft(m.normalised(x, 1.0), drive)


def single_sound(n, rpm, load, over, seed, period=None, start=0, misfire=0.0):
    return warm(m.add(*single_parts(n, rpm, load, over, seed, period, start, misfire).values()), 1.2)


@m.made
def single_loop(rpm, load, over, seconds=6.0, pre=1.0, seed=1, version=VERSION):
    """The single running steadily at `rpm` (a loop): on the throttle (`load`) or on the overrun (`over`)."""
    n = m.ns(pre + seconds) + 64
    return [single_sound(n, m.const(n, rpm), m.const(n, load), m.const(n, over), seed, m.ns(seconds), m.ns(pre))]


@m.made
def single_start(seconds=2.8, seed=1, kicks=2, version=VERSION):
    """A kick start: the lever's clank and the crank turned over against compression (a kick that doesn't catch, then
    one that does), the first firings ragged as it catches, a blip of the throttle, and the idle."""
    n = m.ns(seconds)
    rng = m.Rand(seed + 10)
    out = m.zeros(n)
    t = 0.04
    for k in range(kicks):
        last = k == kicks - 1
        m.add_at(out, m.clank(m.ns(0.4), 0, seed + 20 + k, 820.0, m.SHELL, 0.16, 0.7, 0.35), m.ns(t))
        # The crank spun by the kick: compression's thump and the air pushed through the pipe.
        for j, at in enumerate((0.07, 0.19)):
            m.add_at(out, m.thump(m.ns(0.18), 0, 110.0, 70.0, 0.1, 0.35 * rng.uni(0.7, 1.0), seed + 30 + j), m.ns(t + at))
            m.add_at(out, m.burst(m.ns(0.2), 0, seed + 40 + j, 0.004, 0.02, 0.09, 120.0, 1400.0, 0.08), m.ns(t + at))
        if not last:
            t += 0.85
    catch = t + 0.12
    rpm = m.env(n, [(0.0, 0.0), (catch, 900.0), (catch + 0.12, 2200.0), (catch + 0.45, 1500.0), (catch + 0.75, 3900.0),
                    (catch + 1.05, 1700.0), (seconds, SINGLE_IDLE)])
    load = m.env(n, [(0.0, 0.0), (catch + 0.55, 0.0), (catch + 0.65, 0.7), (catch + 0.8, 0.0)])
    run = single_sound(n, rpm, load, m.const(n, 0.0), seed, misfire=0.0)
    gate = m.env(n, [(0.0, 0.0), (catch - 0.02, 0.0), (catch + 0.03, 1.0)])
    # The first few firings ragged: a little more misfire while it warms (by gating a random few).
    rough = m.env(n, [(0.0, 1.0), (catch + 0.35, 1.0), (catch + 0.5, 0.0)])
    stutter = m.mul(m.lowpass1(m.white(n, seed + 50), 25.0), rough)
    gate = [g * (1.0 - min(0.85, max(0.0, 6.0 * s))) for g, s in zip(gate, stutter)]
    return [m.add(out, m.mul(run, gate))]


@m.made
def single_stop(seconds=1.4, seed=1, version=VERSION):
    """The kill switch: the firing stops at once with one late pop, the crank runs down through its last compressions."""
    n = m.ns(seconds)
    rpm = m.env(n, [(0.0, SINGLE_IDLE), (0.06, 1300.0), (0.45, 450.0), (0.7, 0.0)])
    firing = m.env(n, [(0.0, 1.0), (0.03, 0.15), (0.2, 0.04), (0.5, 0.0)])
    out = m.mul(single_sound(n, rpm, m.const(n, 0.0), m.const(n, 0.3), seed), firing)
    m.add_at(out, m.burst(m.ns(0.3), 0, seed + 40, 0.001, 0.008, 0.08, 150.0, 3000.0, 0.5, 0.3), m.ns(0.16))
    m.add_at(out, m.thump(m.ns(0.25), 0, 95.0, 60.0, 0.14, 0.3, seed + 41), m.ns(0.62))
    return [out]


@m.made
def gear_clunk(seconds=0.5, seed=1, heavy=0.0, version=VERSION):
    """A gear change: the selector's click and the dogs going in (a bike's light clunk; `heavy`: a truck box's thud and
    its lever's swish)."""
    n = m.ns(seconds)
    rng = m.Rand(seed)
    f0 = 420.0 - 170.0 * heavy
    out = m.clank(n, m.ns(0.01), seed + 1, f0 * rng.uni(0.92, 1.08), m.SHELL, 0.12 + 0.2 * heavy, 0.75, 0.6)
    out = m.add(out, m.thump(n, m.ns(0.012), 130.0 - 45.0 * heavy, 70.0 - 25.0 * heavy, 0.07 + 0.08 * heavy, 0.8, seed + 2))
    out = m.add(out, m.burst(n, m.ns(0.006), seed + 3, 0.0005, 0.001, 0.012, 3000.0, 9000.0, 0.25))
    if heavy:
        out = m.add(out, m.scale(m.burst(n, 0, seed + 4, 0.02, 0.03, 0.06, 1200.0, 4500.0, 0.12, 0.3), heavy))
    return [out]


# ---------------------------------------------------------------- the TOGG's turbo-diesel six

CYLINDERS = (1.0, 0.93, 1.06, 0.97, 1.04, 0.91)
BLOCK = [(980.0, 0.014, 1.0), (1420.0, 0.012, 0.9), (1900.0, 0.011, 0.8), (2450.0, 0.009, 0.7), (3050.0, 0.008, 0.6),
         (3800.0, 0.007, 0.5), (4700.0, 0.006, 0.4), (5900.0, 0.005, 0.3)]


def diesel_parts(n, rpm, load, fuel, seed, period=None, start=0, cold=0.0):
    """A 6.7 l turbo-diesel inline six at `rpm`, `load` (0-1) and `fuel` (0-1: 0 cuts it, the overrun): six firings every
    two turns, each cylinder a little its own; the turbine smoothing the pulses into two open stacks and a big muffler;
    the block's knock (clatter) at every firing, harder at idle, light load and cold (`cold`); a lope at the cycle's
    rate; the valves, the timing gears, the fan, the intake roar; a rain cap flapping at idle."""
    rng = m.Rand(seed)
    jit = m.wander(n, seed + 1, 1.1, period)
    span = DIESEL_REDLINE - DIESEL_IDLE
    cycle = [r / 120.0 * (1.0 + 0.004 * j) for r, j in zip(rpm, jit)]
    ph = m.phase(cycle, period, start)
    evs = crossings(ph, 6)
    rows = table(rng, periodic_count(ph, 6, period, start), 4)
    burn, air, knock, inject, cap = [], [], [], [], []
    for i, k in evs:
        u = rows[k % len(rows)]
        c = CYLINDERS[k % 6]
        f, load_i, r = fuel[i], load[i], rpm[i]
        cold_i = cold[i] if hasattr(cold, "__len__") else cold
        idleness = max(0.0, 1.0 - (r - DIESEL_IDLE) / (0.45 * span))
        a = (0.3 + 0.7 * load_i) * c * (1.0 + 0.05 * (2.0 * u[0] - 1.0)) * f
        if cold_i and u[1] < 0.25 * cold_i:
            a *= 0.15
        burn.append(a)
        air.append(0.22 * sqrt(max(r, 1.0) / DIESEL_REDLINE))
        kn = (0.45 + 0.55 * (1.0 - load_i)) * (1.0 + 0.45 * idleness + 0.9 * cold_i) * c * (1.0 + 0.18 * (2.0 * u[1] - 1.0))
        knock.append(kn * f + 0.12 * (1.0 - f))
        inject.append(0.4 * f * c)
        cap.append(0.6 * u[3] if u[2] < 0.22 * idleness * (1.0 - load_i) * f else 0.0)
    excite = m.add(place(n, evs, burn, kernel(0.7)), place(n, evs, air, kernel(2.2)))
    gas = m.mul(m.chain(m.white(n, seed + 2, period), ("bp", 520.0, 0.6)), m.decay(impulses(n, evs, burn), 6.0))
    raw = m.add(excite, m.mul(gas, [0.05 + 0.15 * l for l in load]))
    # The turbine takes the pulses' edge off; two stacks of slightly different length beat; the muffler's big body.
    turbo = m.add(m.chain(raw, ("lp", 650.0, 0.7)), m.scale(m.chain(raw, ("hp", 650.0)), 0.3))
    stacks = m.add(pipe(turbo, 2 * 1.70 / 470.0, -0.45, 1400.0), pipe(turbo, 2 * 1.62 / 470.0, -0.42, 1400.0))
    muffled = m.chain(stacks, ("peak", 58.0, 0.9, 6.0), ("peak", 150.0, 1.2, 3.0), ("lp", 950.0, 0.7), ("hs", 2500.0, 0.7, -6.0))
    exhaust = m.add(muffled, m.scale(m.chain(stacks, ("hp", 1500.0)), 0.08))
    # The firing's body and the lope (the cycle's own rate, from the cylinders' differences), felt more than heard.
    fire = [6.0 * p for p in ph]
    body = m.lowpass1(m.mul(m.tones(fire, ((1, 1.0, 0.0), (2, 0.35, 0.2), (3, 0.12, 0.45))),
                            [(0.25 + 0.75 * l) * (0.3 + 0.7 * f) for l, f in zip(load, fuel)]), 220.0)
    lope = m.lowpass1(m.mul(m.tones(ph, ((1, 1.0, 0.0), (2, 0.6, 0.3), (3, 0.4, 0.1))), fuel), 120.0)
    # The clatter: each firing's knock rings the block; the injectors tick; the rain cap flaps.
    hits = impulses(n, evs, knock, delay=int(0.0006 * RATE))
    rap = m.mul(m.chain(m.white(n, seed + 3, period), ("bp", 2200.0, 0.5)), m.decay(hits, 1.2))
    clatter = m.add(m.ring(m.add(hits, m.scale(rap, 0.5)), BLOCK, n), m.scale(rap, 0.8))
    injectors = m.mul(m.chain(m.white(n, seed + 4, period), ("hp", 5200.0)), m.decay(impulses(n, evs, inject), 0.5))
    flaps = m.ring(impulses(n, evs, cap, delay=int(0.004 * RATE)), [(2100.0, 0.06, 1.0), (3340.0, 0.04, 0.6), (4500.0, 0.03, 0.4)], n)
    valves = crossings(ph, 12, 0.5)
    tick = m.ring(impulses(n, valves, [1.0] * len(valves)), [(3600.0, 0.01, 1.0), (5400.0, 0.007, 0.5)], n)
    crank = [2.0 * p for p in ph]
    gears = m.mul(m.tones(crank, ((36, 1.0, 0.0), (72, 0.25, 0.4))), [0.003 * r / DIESEL_REDLINE for r in rpm])
    fan = m.mul(m.chain(m.white(n, seed + 5, period), ("bp", 520.0, 0.6)), [0.08 * (r / DIESEL_REDLINE) ** 2 for r in rpm])
    blades = m.mul(m.tones(crank, ((8, 1.0, 0.0),)), [0.006 * (r / DIESEL_REDLINE) ** 2 for r in rpm])
    intake = m.mul(m.chain(m.white(n, seed + 6, period), ("bp", 600.0, 0.8)), [0.15 * l * r / DIESEL_REDLINE for l, r in zip(load, rpm)])
    return {"exhaust": exhaust, "body": m.scale(body, 0.12), "lope": m.scale(lope, 0.05), "clatter": m.scale(clatter, 0.3),
            "injectors": m.scale(injectors, 0.03), "flaps": m.scale(flaps, 0.05), "tick": m.scale(tick, 0.025),
            "gears": gears, "fan": fan, "blades": blades, "intake": intake}


def diesel_sound(n, rpm, load, fuel, seed, period=None, start=0, cold=0.0):
    return warm(m.add(*diesel_parts(n, rpm, load, fuel, seed, period, start, cold).values()), 1.3)


@m.made
def diesel_loop(rpm, load, fuel=1.0, seconds=6.0, pre=1.0, seed=1, version=VERSION):
    """The six running steadily at `rpm` and `load` (a loop); `fuel` 0 is the overrun (fuel cut)."""
    n = m.ns(pre + seconds) + 64
    return [diesel_sound(n, m.const(n, rpm), m.const(n, load), m.const(n, fuel), seed, m.ns(seconds), m.ns(pre))]


def _starter(n, seed, crank_rpm, until):
    """The starter motor: its whine and the pinion's mesh, sagging at every compression (six a cycle), its brushes."""
    hz = [0.0] * n
    cyc = m.phase([r / 120.0 for r in crank_rpm])
    for i in range(n):
        squeeze = 0.5 + 0.5 * cos(TAU * 6.0 * cyc[i])
        hz[i] = 52.0 * (1.0 - 0.14 * squeeze) if i < until else 0.0
    ph = m.phase(hz)
    on = m.env(n, [(0.0, 0.0), (0.03, 1.0), (until / RATE - 0.05, 1.0), (until / RATE + 0.12, 0.0)])
    whine = m.mul(m.tones(ph, ((1, 0.6, 0.0), (2, 0.4, 0.2), (11, 0.35, 0.4), (22, 0.12, 0.7))), on)
    brush = m.mul(m.chain(m.white(n, seed), ("bp", 2400.0, 1.0)), m.scale(on, 0.12))
    return m.add(m.scale(whine, 0.18), brush)


@m.made
def diesel_start(seconds=3.6, seed=1, catch=1.25, version=VERSION):
    """A cold crank start: the solenoid's clunk, the starter turning it over (its whine sagging at each compression,
    air chuffing through the stacks), the first firings ragged and loud with clatter, a flare of revs, the idle."""
    n = m.ns(seconds)
    out = m.contactor(n, m.ns(0.02), seed + 1, size=1.4, arc=0.2)
    crank = m.env(n, [(0.0, 0.0), (0.08, 170.0), (catch, 200.0), (catch + 0.25, 820.0), (catch + 0.6, 1150.0),
                      (catch + 1.3, 820.0), (seconds, DIESEL_IDLE)])
    fuel = m.env(n, [(0.0, 0.0), (catch - 0.05, 0.0), (catch + 0.05, 1.0)])
    load = m.env(n, [(0.0, 0.0), (catch, 0.0), (catch + 0.15, 0.6), (catch + 0.6, 0.15), (catch + 1.2, 0.0)])
    cold = m.env(n, [(0.0, 1.0), (catch + 0.8, 0.6), (seconds, 0.3)])
    # The engine (no fuel while cranking: only its air and its metal), its clatter cold.
    run = diesel_sound(n, crank, load, fuel, seed + 3, cold=cold)
    starter = _starter(n, seed + 2, crank, m.ns(catch + 0.15))
    out = m.add(out, run, starter)
    # The pinion letting go: a zing.
    m.add_at(out, m.burst(m.ns(0.25), 0, seed + 4, 0.002, 0.01, 0.12, 1800.0, 6000.0, 0.12, 0.4), m.ns(catch + 0.15))
    return [out]


@m.made
def diesel_stop(seconds=2.2, seed=1, version=VERSION):
    """The fuel cut: the firing stops, the revs run down through the pumping of its air, the last compression rocks the
    engine back on its mounts (a thud and a rattle), the turbo whirs down, a tick or two."""
    n = m.ns(seconds)
    rpm = m.env(n, [(0.0, DIESEL_IDLE), (0.1, 690.0), (0.7, 260.0), (1.0, 0.0)])
    fuel = m.env(n, [(0.0, 1.0), (0.05, 0.0)])
    out = diesel_sound(n, rpm, m.const(n, 0.0), fuel, seed)
    out = m.mul(out, m.env(n, [(0.0, 1.0), (0.9, 0.6), (1.05, 0.0)]))
    m.add_at(out, m.thump(m.ns(0.5), 0, 60.0, 38.0, 0.3, 0.9, seed + 1), m.ns(0.98))
    m.add_at(out, m.clank(m.ns(0.5), 0, seed + 2, 330.0, m.PLATE, 0.3, 0.4, 0.25), m.ns(1.0))
    for j, at in enumerate((1.4, 1.85)):
        m.add_at(out, m.tick(m.ns(0.1), 0, seed + 5 + j, 3100.0, 0.08), m.ns(at))
    return [out]


@m.made
def turbo_whistle(seconds=6.0, pre=1.0, seed=1, version=VERSION):
    """The turbo spooled up (a loop the game pitches and swells by boost): the compressor's whistle with its slight
    wander, its second harmonic, the air rushing into it."""
    n = m.ns(pre + seconds) + 64
    period, start = m.ns(seconds), m.ns(pre)
    j = m.wander(n, seed + 1, 3.0, period)
    ph = m.phase([3300.0 * (1.0 + 0.004 * v) for v in j], period, start)
    tone = m.tones(ph, ((1, 1.0, 0.0), (2, 0.18, 0.3)))
    air = m.chain(m.white(n, seed + 2, period), ("bp", 3000.0, 1.6))
    hiss = m.chain(m.white(n, seed + 3, period), ("hp", 4500.0))
    swirl = [1.0 + 0.15 * v for v in m.wander(n, seed + 4, 8.0, period)]
    return [m.mul(m.add(m.scale(tone, 0.25), m.scale(air, 0.5), m.scale(hiss, 0.12)), swirl)]


@m.made
def turbo_release(seconds=1.0, seed=1, version=VERSION):
    """The throttle let go at boost: the compressor's air surging back (a fluttering whoosh) as the whistle falls."""
    n = m.ns(seconds)
    rng = m.Rand(seed)
    rate = 16.0 + 6.0 * rng.uni()
    flutter = [0.55 + 0.45 * cos(TAU * rate * i / RATE) ** 2 for i in range(n)]
    whoosh = m.burst(n, 0, seed + 1, 0.02, 0.05, 0.55, 900.0, 5000.0, 1.0, 0.4)
    ph = m.phase(m.env(n, [(0.0, 3400.0), (0.6, 1500.0)], "exp"))
    whistle = m.mul(m.tones(ph, ((1, 1.0, 0.0),)), m.env(n, [(0.0, 0.25), (0.5, 0.0)]))
    return [m.add(m.mul(whoosh, flutter), m.scale(whistle, 0.4))]


# ---------------------------------------------------------------- tyres: grains of the footstep recordings

NOX = "footstep-packs-20261018/nox"


def _nox(surface, gaits=("run", "walk")):
    banks = sources.NOX_FOOTSTEPS["banks"][surface]
    return [sources.pack(NOX, v["member"]) for g in gaits for v in banks.get(g, [])]


def _decode_all(paths, seconds=40.0):
    """Each recording (its first `seconds`) as an array of floats, scaled to a peak of 1."""
    from array import array
    out = []
    for p in paths:
        x = audiolib.decode(p, RATE, 0.0, seconds)
        peak = max(1e-6, max(x), -min(x))
        out.append(array("f", (v / peak for v in x)))
    return out


def _loud_spots(x, width, floor_db=-20.0):
    """Starting samples of `width` whose level is within floor_db of the recording's loudest (the crunch, not the
    room)."""
    hop = max(1, width // 4)
    levels = []
    for s in range(0, max(1, len(x) - width), hop):
        seg = x[s:s + width:4]
        levels.append((sqrt(sum(v * v for v in seg) / max(1, len(seg))), s))
    if not levels:
        return [0]
    top = max(l for l, _ in levels) or 1e-6
    keep = [s for l, s in levels if 20.0 * math.log10(max(l, 1e-9) / top) > floor_db]
    return keep or [levels[0][1]]


def strew(n, recordings, rate, dur_ms, pitch, gain_sd_db, seed, floor_db=-20.0):
    """Grains of the recordings strewn at `rate` a second round a loop of n samples (wrapping round its end): each of
    a length in dur_ms, read at a pitch in `pitch`, Hann-windowed, its level spread by gain_sd_db."""
    rng = m.Rand(seed)
    out = m.zeros(n)
    width = int(dur_ms[1] * 0.001 * RATE * pitch[1]) + 2
    spots = [_loud_spots(x, width, floor_db) for x in recordings]
    count = int(rate * n / RATE)
    for _ in range(count):
        r = min(len(recordings) - 1, int(rng.uni() * len(recordings)))
        x, sp = recordings[r], spots[r]
        s0 = sp[min(len(sp) - 1, int(rng.uni() * len(sp)))]
        length = int(rng.uni(*dur_ms) * 0.001 * RATE)
        p = rng.uni(*pitch)
        g = 10.0 ** (rng.sym(gain_sd_db) / 20.0)
        t = int(rng.uni() * n)
        last = len(x) - 2
        for j in range(length):
            pos = s0 + j * p
            k = int(pos)
            if k >= last:
                break
            f = pos - k
            w = sin(math.pi * (j + 0.5) / length) ** 2
            out[(t + j) % n] += g * w * (x[k] + f * (x[k + 1] - x[k]))
    return out


def circular(x, *stages):
    """Filters a loop as if it had always played (two passes, the second kept)."""
    n = len(x)
    return m.chain(list(x) + list(x), *stages)[n:]


def cwander(n, seed, hz):
    """machines.wander round a loop of n samples (its filters settled round the loop, so its ends meet)."""
    w = m.white(n, seed, n)
    y = m.lowpass1(m.lowpass1(w + w + w, hz), hz)[2 * n:]
    rms = sqrt(sum(v * v for v in y) / max(1, n)) or 1.0
    return m.scale(y, 0.4 / rms)


def cphase(freq):
    """The phase of a frequency (Hz per sample) round a loop: scaled to a whole number of cycles in it."""
    total = sum(freq) / RATE
    k = max(1, round(total)) / total if total > 0 else 1.0
    return m.phase([f * k for f in freq])


def tread(n, seed, knob_hz, hum, roar, roar_hz=700.0):
    """The tread rolling: its knobs slapping the ground at knob_hz (a buzzing hum), and the road roar under it."""
    period = n
    w = m.white(n, seed, period)
    ph = cphase(m.const(n, knob_hz))
    slap = [0.5 + 0.5 * cos(TAU * p) ** 8 for p in ph]
    buzz = circular(m.mul(w, slap), ("bp", knob_hz * 2.0, 0.8), ("lp", 1800.0))
    rumble = circular(m.white(n, seed + 1, period), ("lp", roar_hz, 0.7), ("hp", 40.0))
    swell = [1.0 + 0.3 * v for v in cwander(n, seed + 2, 1.5)]
    return m.mul(m.add(m.scale(buzz, hum), m.scale(rumble, roar)), swell)


TYRE_GROUNDS = {
    # surface: (recordings, grains a second, grain ms, pitch, level spread dB, grain level, tread hum, roar, roar Hz,
    #           filters on the grains)
    "gravel": (lambda: [sources.bsb(i) for i in ("0510", "1117", "3202", "3216", "0085", "0839")],
               150.0, (25.0, 70.0), (0.9, 1.5), 5.0, 0.55, 0.12, 0.35, 500.0, (("hp", 120.0),)),
    "dirt": (lambda: _nox("dirt"), 90.0, (40.0, 110.0), (0.8, 1.15), 5.0, 0.45, 0.15, 0.55, 320.0, (("lp", 3200.0, 0.7), ("hp", 60.0))),
    "grass": (lambda: _nox("grass"), 110.0, (40.0, 120.0), (0.9, 1.3), 4.0, 0.5, 0.06, 0.3, 300.0, (("hp", 200.0),)),
    "sand": (lambda: _nox("sand"), 120.0, (40.0, 100.0), (0.9, 1.25), 4.0, 0.5, 0.05, 0.35, 260.0, (("hp", 150.0),)),
    "snow": (lambda: _nox("snow"), 100.0, (40.0, 110.0), (0.9, 1.25), 4.0, 0.5, 0.04, 0.3, 260.0, (("hp", 120.0),)),
    "rock": (lambda: _nox("rock"), 22.0, (20.0, 60.0), (1.0, 1.6), 6.0, 0.3, 0.5, 0.6, 900.0, (("hp", 300.0),)),
    "water": (lambda: _nox("water") + [sources.bsb(i) for i in ("0690", "1529", "1530", "1531", "1532")],
              45.0, (60.0, 200.0), (0.85, 1.2), 5.0, 0.8, 0.0, 0.2, 400.0, (("hp", 120.0),)),
}


@m.made
def tyre_loop(surface, seconds=8.0, seed=1, knob_hz=125.0, version=VERSION):
    """Tyres rolling on a ground at the reference speed (10 m/s): the ground's grains under the tread's hum and roar (a
    loop; the game pitches and swells it by speed). On sand a hiss of the grains blown back, on water a spray."""
    recs, rate, dur, pitch, sd, level, hum, roar, roar_hz, filters = TYRE_GROUNDS[surface]
    n = m.ns(seconds)
    grains = circular(strew(n, _decode_all(recs()), rate, dur, pitch, sd, seed + 1), *filters)
    out = m.add(m.scale(grains, level), tread(n, seed + 2, knob_hz, hum, roar, roar_hz))
    if surface == "sand":
        out = m.add(out, m.scale(circular(m.white(n, seed + 3, n), ("bp", 3200.0, 0.5)), 0.12))
    if surface == "water":
        spray = m.mul(circular(m.white(n, seed + 4, n), ("hp", 2200.0)), [0.5 + 0.5 * v for v in cwander(n, seed + 5, 4.0)])
        out = m.add(out, m.scale(spray, 0.15))
    if surface == "rock":
        # Stones flicked against the underside now and then.
        pings = m.zeros(n)
        rng = m.Rand(seed + 6)
        for _ in range(int(3 * seconds)):
            m.add_at(pings, m.clank(m.ns(0.08), 0, seed + 7 + int(rng.uni() * 1000), rng.uni(2400.0, 4200.0), m.SHELL, 0.03, 0.9, 0.12),
                     int(rng.uni() * (n - m.ns(0.08))))
        out = m.add(out, pings)
    return [out]


@m.made
def skid_loop(loose=1, seconds=6.0, seed=1, version=VERSION):
    """A tyre sliding or spinning: on loose ground stones flung and scrabbled (gravel grains, dense and bright, over a
    chattering scrape); on hard ground the rubber scrubbing and squealing, its knobs chattering."""
    n = m.ns(seconds)
    chatter = [0.6 + 0.4 * cos(TAU * 28.0 * i / RATE) ** 2 for i in range(n)]
    if loose:
        recs = _decode_all([sources.bsb(i) for i in ("0510", "1117", "3202", "3216", "0839")])
        grains = circular(strew(n, recs, 280.0, (15.0, 50.0), (1.1, 1.8), 5.0, seed + 1), ("hp", 300.0))
        scrape = m.mul(circular(m.white(n, seed + 2, n), ("bp", 1400.0, 0.8)), chatter)
        return [m.add(m.scale(grains, 0.6), m.scale(scrape, 0.25))]
    j = cwander(n, seed + 3, 6.0)
    ph = cphase([780.0 * (1.0 + 0.08 * v) for v in j])
    squeal = m.tones(ph, ((1, 1.0, 0.0), (2, 0.3, 0.2), (3, 0.12, 0.5)))
    scrub = m.mul(circular(m.white(n, seed + 4, n), ("bp", 1200.0, 0.9)), chatter)
    return [m.add(m.scale(m.mul(squeal, chatter), 0.15), m.scale(scrub, 0.5))]


# ---------------------------------------------------------------- the suspension

def _knock(n, seed, heavy):
    rng = m.Rand(seed)
    out = m.thump(n, m.ns(0.005), (150.0 - 70.0 * heavy) * rng.uni(0.9, 1.1), 80.0 - 40.0 * heavy, 0.08 + 0.1 * heavy, 1.0, seed + 1)
    out = m.add(out, m.clank(n, m.ns(0.008), seed + 2, (650.0 - 420.0 * heavy) * rng.uni(0.9, 1.1), m.SHELL, 0.12 + 0.25 * heavy,
                             0.5, 0.45))
    twang = m.ring(m.pulse(n, m.ns(0.01), 0.8), [(140.0 + 80.0 * (1 - heavy), 0.3, 0.3), (300.0, 0.2, 0.15), (470.0, 0.12, 0.08)], n)
    out = m.add(out, m.scale(twang, 0.6 * (0.4 + heavy)))
    rattle = m.mul(m._rattle(n, seed + 3, m.const(n, 40.0), 0.25), m.env(n, [(0.0, 0.0), (0.02, 1.0), (0.25, 0.0)]))
    if heavy:
        out = m.add(out, m.scale(m.clank(n, m.ns(0.015), seed + 4, 330.0, m.PLATE, 0.3, 0.4, 0.3), heavy))
    return m.add(out, rattle)


@m.made
def suspension_knock(seconds=0.7, seed=1, heavy=0.0, version=VERSION):
    """A wheel hitting its bump stop: a low thud, the frame's clank, a spring's twang and loose things rattling (a bike's
    light knock; `heavy`: a truck's chassis slam)."""
    return [_knock(m.ns(seconds), seed, heavy)]


@m.made
def suspension_creak(seconds=0.6, seed=1, heavy=0.0, version=VERSION):
    """A spring seat and its rubber bushes creaking as the suspension works: stick-slip, a run of small slips at a
    wandering rate, ringing the bush and the seat."""
    n = m.ns(seconds)
    rng = m.Rand(seed)
    rate = m.env(n, [(0.0, rng.uni(50.0, 90.0)), (seconds * 0.5, rng.uni(90.0, 150.0)), (seconds, rng.uni(40.0, 80.0))])
    ph = m.phase(rate)
    slips = [(i, k) for i, k in crossings(ph)]
    amps = [rng.uni(0.4, 1.0) for _ in slips]
    shape = m.env(n, [(0.0, 0.0), (0.06, 1.0), (seconds * 0.7, 0.8), (seconds, 0.0)])
    x = m.mul(impulses(n, slips, amps), shape)
    s = 1.0 - 0.45 * heavy
    out = m.ring(x, [(300.0 * s, 0.04, 1.0), (760.0 * s, 0.03, 0.7), (1300.0 * s, 0.02, 0.5), (1900.0 * s, 0.015, 0.4),
                     (2650.0 * s, 0.01, 0.3)], n)
    return [out]


@m.made
def suspension_land(seconds=1.0, seed=1, heavy=0.0, version=VERSION):
    """Landing from a jump: the tyres' thump, the springs slammed to their stops, the frame and its load rattling, a
    little ground thrown up."""
    n = m.ns(seconds)
    out = m.thump(n, m.ns(0.004), 90.0 - 30.0 * heavy, 45.0 - 15.0 * heavy, 0.2 + 0.15 * heavy, 1.0, seed + 1)
    for j, at in enumerate((0.01, 0.035)):
        m.add_at(out, m.scale(_knock(n - m.ns(at), seed + 10 + j, heavy), 0.6), m.ns(at))
    out = m.add(out, m.clank(n, m.ns(0.02), seed + 3, 260.0 + 300.0 * (1 - heavy), m.PLATE, 0.35, 0.45, 0.35))
    out = m.add(out, m.mul(m._rattle(n, seed + 4, m.const(n, 55.0), 0.35), m.env(n, [(0.0, 0.0), (0.03, 1.0), (0.5, 0.0)])))
    out = m.add(out, m.burst(n, m.ns(0.01), seed + 5, 0.005, 0.03, 0.3, 150.0, 2500.0, 0.25, 0.5))
    return [out]


if __name__ == "__main__":
    import time
    t = time.time()
    print(single_loop("test_single", rpm=SINGLE_IDLE, load=0.0, over=0.0, seconds=2.0, pre=0.5), "%.1f s" % (time.time() - t))
    t = time.time()
    print(diesel_loop("test_diesel", rpm=DIESEL_IDLE, load=0.0, seconds=2.0, pre=0.5), "%.1f s" % (time.time() - t))
