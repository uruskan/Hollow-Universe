"""The Iron Wake's machinery, made in code (IRON_WAKE.md §10, GROUND.md §9.2): drives, pumps, contactors, hydraulics,
servos, chains and ratchets, steel that rings, rocket motors, alarms, and the impulse responses of steel spaces.

The CC0 libraries on this PC hold no ship drives, turbines, cannons, klaxons or alarm bells, so these are modelled the
way the machines make their sound (SHIP_FEEL.md: contactors, pumps, rising machinery, a settled hull resonance):
- a rotating machine is a shaft whose phase drives its orders: the shaft's hum and harmonics, the turbine's blade-pass
  whine (blades x shaft) with its sidebands, a gear's mesh (teeth x shaft), a pump's strokes;
- combustion and gas are noise, shaped by filters, moved by slow turbulence, with crackle under load;
- steel is modal: a strike excites resonances, each with its own frequency and decay; the hull is a few low modes that
  the shaft drives (they swell as the drive spools through them, then settle);
- a contactor is a coil's thump, an armature's clack, the contacts' arc and the coil's hum.
Recordings are layered over these in events.py (iron_wake()).

Loops are made periodic here: every random source repeats with the loop's period and every tone completes a whole
number of cycles in it, so the pipeline only cuts one period once the filters have settled (build.make_loop without an
overlap) and the seam is not there to hear.

Deterministic (synth.Lcg's numbers, Python's float maths); each sound is cached under art/.cache/audio/synth by a hash
of its arguments.
"""

import itertools
import math
import operator
import os
import sys
from array import array

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import audiolib  # noqa: E402
import synth  # noqa: E402

RATE = synth.RATE
VERSION = 1
TAU = 2.0 * math.pi
sin, cos, exp, log, sqrt = math.sin, math.cos, math.exp, math.log, math.sqrt


# ---------------------------------------------------------------- buffers and numbers

def ns(seconds):
    """Samples in `seconds`."""
    return int(round(seconds * RATE))


def zeros(n):
    return [0.0] * n


class Rand:
    """Deterministic random numbers (synth.Lcg's generator)."""

    def __init__(self, seed):
        self.g = synth.Lcg(seed)

    def uni(self, lo=0.0, hi=1.0):
        return lo + (hi - lo) * (0.5 + 0.5 * self.g.next())

    def sym(self, depth=1.0):
        return depth * self.g.next()


def white(n, seed, period=None):
    """White noise in [-1, 1); with `period` (samples) it repeats every period."""
    s = (seed * 2654435761 + 1) & 0xFFFFFFFF
    m = n if not period else min(n, period)
    out = zeros(n)
    for i in range(m):
        s = (1664525 * s + 1013904223) & 0xFFFFFFFF
        out[i] = s / 2147483648.0 - 1.0
    for i in range(m, n):
        out[i] = out[i - period]
    return out


def mul(a, b):
    return list(map(operator.mul, a, b))


def add(*parts):
    """The sum of signals (the length of the shortest)."""
    out = parts[0]
    for p in parts[1:]:
        out = list(map(operator.add, out, p))
    return out


def scale(a, k):
    return list(map(float(k).__mul__, a))


def add_at(dst, src, at=0, gain=1.0):
    """Adds `src` x gain into `dst` from sample `at` (in place; what falls outside is dropped)."""
    if at >= len(dst):
        return dst
    if at < 0:
        src = src[-at:]
        at = 0
    m = min(len(src), len(dst) - at)
    if m <= 0:
        return dst
    part = src[:m] if gain == 1.0 else list(map(float(gain).__mul__, src[:m]))
    dst[at:at + m] = list(map(operator.add, dst[at:at + m], part))
    return dst


def peak(x):
    return max(max(x), -min(x)) if len(x) else 0.0


def normalised(x, top=0.89):
    m = peak(x)
    return scale(x, top / m) if m > 0 else x


def soft(x, drive):
    """Soft saturation (tanh), the level kept."""
    if drive <= 0:
        return x
    k = 1.0 / math.tanh(drive)
    return [math.tanh(drive * v) * k for v in x]


# ---------------------------------------------------------------- envelopes and phases

def env(n, points, curve="lin"):
    """Through (seconds, value) points: linear, or exponential (`curve="exp"`, for values above 0); held after the last."""
    pts = sorted(points)
    if pts[0][0] > 0:
        pts.insert(0, (0.0, pts[0][1]))
    out = zeros(n)
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        i0, i1 = ns(t0), min(n, ns(t1))
        span = max(1, ns(t1) - i0)
        if i1 <= i0:
            continue
        if curve == "exp" and v0 > 0 and v1 > 0:
            r = (v1 / v0) ** (1.0 / span)
            v = v0
            for i in range(i0, i1):
                out[i] = v
                v *= r
        else:
            d = (v1 - v0) / span
            for k in range(i1 - i0):
                out[i0 + k] = v0 + d * k
    last = pts[-1][1]
    for i in range(min(n, ns(pts[-1][0])), n):
        out[i] = last
    return out


def const(n, v):
    return [float(v)] * n


def phase(freq, period=None, start=0):
    """The running phase (cycles) of a frequency (Hz) per sample. With `period`, scaled so it advances a whole number
    of cycles in every period from `start` on (a periodic frequency then gives periodic tones)."""
    inv = 1.0 / RATE
    ph = list(itertools.accumulate(map(inv.__mul__, freq), initial=0.0))[:len(freq)]
    if period and start + period < len(ph):
        total = ph[start + period] - ph[start]
        if total > 0:
            ph = scale(ph, max(1, round(total)) / total)
    return ph


def tone(ph, order, amp=1.0, offset=0.0):
    """amp x sin(2 pi (order x phase + offset)); `amp` a number or a signal."""
    w = TAU * order
    o = TAU * offset
    s = list(map(sin, map(o.__add__, map(w.__mul__, ph)))) if offset else list(map(sin, map(w.__mul__, ph)))
    return mul(s, amp) if hasattr(amp, "__len__") else (scale(s, amp) if amp != 1.0 else s)


_TONES = {}


def tones(ph, spec):
    """A sum of tones on one phase, in one pass: spec = [(order, amp, offset in cycles)]."""
    key = tuple(spec)
    f = _TONES.get(key)
    if f is None:
        terms = " + ".join("%r * sin(%r * p + %r)" % (float(a), TAU * o, TAU * off) for o, a, off in spec)
        f = eval("lambda ph: [%s for p in ph]" % terms, {"sin": sin})  # noqa: S307 (our own numbers)
        _TONES[key] = f
    return f(ph)


# ---------------------------------------------------------------- filters

def lowpass1(x, hz):
    a = exp(-TAU * hz / RATE)
    b = 1.0 - a
    y = 0.0
    out = zeros(len(x))
    for i, v in enumerate(x):
        y = b * v + a * y
        out[i] = y
    return out


def highpass1(x, hz):
    return list(map(operator.sub, x, lowpass1(x, hz)))


def biquad(x, kind, hz, q=0.7071, db=0.0):
    """An RBJ biquad: lp, hp, bp (0 dB at its centre), peak, notch, ls, hs."""
    w = TAU * min(hz, RATE * 0.45) / RATE
    cw, sw = cos(w), sin(w)
    alpha = sw / (2.0 * q)
    big = 10.0 ** (db / 40.0)
    if kind == "lp":
        b0, b1, b2, a0, a1, a2 = (1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + alpha, -2 * cw, 1 - alpha
    elif kind == "hp":
        b0, b1, b2, a0, a1, a2 = (1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + alpha, -2 * cw, 1 - alpha
    elif kind == "bp":
        b0, b1, b2, a0, a1, a2 = alpha, 0.0, -alpha, 1 + alpha, -2 * cw, 1 - alpha
    elif kind == "notch":
        b0, b1, b2, a0, a1, a2 = 1.0, -2 * cw, 1.0, 1 + alpha, -2 * cw, 1 - alpha
    elif kind == "peak":
        b0, b1, b2 = 1 + alpha * big, -2 * cw, 1 - alpha * big
        a0, a1, a2 = 1 + alpha / big, -2 * cw, 1 - alpha / big
    else:
        sq = 2 * sqrt(big) * alpha
        if kind == "ls":
            b0 = big * ((big + 1) - (big - 1) * cw + sq)
            b1 = 2 * big * ((big - 1) - (big + 1) * cw)
            b2 = big * ((big + 1) - (big - 1) * cw - sq)
            a0 = (big + 1) + (big - 1) * cw + sq
            a1 = -2 * ((big - 1) + (big + 1) * cw)
            a2 = (big + 1) + (big - 1) * cw - sq
        else:
            b0 = big * ((big + 1) + (big - 1) * cw + sq)
            b1 = -2 * big * ((big - 1) + (big + 1) * cw)
            b2 = big * ((big + 1) + (big - 1) * cw - sq)
            a0 = (big + 1) - (big - 1) * cw + sq
            a1 = 2 * ((big - 1) - (big + 1) * cw)
            a2 = (big + 1) - (big - 1) * cw - sq
    b0, b1, b2, a1, a2 = b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0
    z1 = z2 = 0.0
    out = zeros(len(x))
    for i, v in enumerate(x):
        y = b0 * v + z1
        z1 = b1 * v - a1 * y + z2
        z2 = b2 * v - a2 * y
        out[i] = y
    return out


def chain(x, *stages):
    """Filters in turn: ("lp", hz[, q]), ("hp", hz[, q]), ("bp", hz, q), ("peak", hz, q, db), ("ls"/"hs", hz, q, db),
    ("lp1", hz), ("hp1", hz)."""
    for st in stages:
        kind = st[0]
        if kind == "lp1":
            x = lowpass1(x, st[1])
        elif kind == "hp1":
            x = highpass1(x, st[1])
        else:
            x = biquad(x, kind, *st[1:])
    return x


def svf(x, hz, q=0.7071, mode="lp"):
    """A state-variable filter whose cut-off may move (`hz`: a number or an array, read every 16 samples); "bp" is
    0 dB at its centre."""
    n = len(x)
    out = zeros(n)
    k = 1.0 / q
    moving = hasattr(hz, "__len__")
    ic1 = ic2 = 0.0
    a1 = a2 = a3 = 0.0
    sel = {"lp": 0, "bp": 1, "hp": 2}[mode]
    for i in range(n):
        if i == 0 or (moving and (i & 15) == 0):
            f = hz[i] if moving else hz
            g = math.tan(math.pi * min(max(f, 5.0), RATE * 0.45) / RATE)
            a1 = 1.0 / (1.0 + g * (g + k))
            a2 = g * a1
            a3 = g * a2
        v0 = x[i]
        v3 = v0 - ic2
        v1 = a1 * ic1 + a2 * v3
        v2 = ic2 + a2 * ic1 + a3 * v3
        ic1 = 2.0 * v1 - ic1
        ic2 = 2.0 * v2 - ic2
        out[i] = v2 if sel == 0 else (k * v1 if sel == 1 else v0 - k * v1 - v2)
    return out


def decay(x, ms):
    """Each sample of `x` decays exponentially over `ms` (a one-pole accumulator: impulses become little bursts)."""
    a = exp(-1.0 / (ms * 0.001 * RATE))
    y = 0.0
    out = zeros(len(x))
    for i, v in enumerate(x):
        y = v + a * y
        out[i] = y
    return out


def wander(n, seed, hz, period=None):
    """Smooth random movement of about +-1 (RMS 0.4), changing at about `hz`."""
    y = lowpass1(lowpass1(white(n, seed, period), hz), hz)
    rms = sqrt(sum(v * v for v in y) / max(1, n)) or 1.0
    return scale(y, 0.4 / rms)


# ---------------------------------------------------------------- strikes, steel and impulses

def pulse(n, at, width_ms=0.4, amp=1.0):
    """A half-sine pulse at sample `at` with unit area: an impulse with a width (the wider, the softer the strike)."""
    out = zeros(n)
    w = max(1, int(width_ms * 0.001 * RATE))
    k0 = amp * math.pi / (2.0 * w)
    for k in range(w):
        if 0 <= at + k < n:
            out[at + k] = k0 * sin(math.pi * (k + 0.5) / w)
    return out


def ring(excite, modes, n=None):
    """Steel ringing: each mode (Hz, t60 in s, gain) a two-pole resonator driven by `excite` (gain = the amplitude it
    rings at after a unit impulse)."""
    n = n or len(excite)
    out = zeros(n)
    m = min(n, len(excite))
    first = next((i for i in range(m) if excite[i] != 0.0), m)
    for hz, t60, g in modes:
        if hz <= 0 or hz >= RATE * 0.45 or g == 0:
            continue
        r = exp(-6.907755 / (max(t60, 0.002) * RATE))
        w = TAU * hz / RATE
        a1, a2, b = 2.0 * r * cos(w), r * r, g * sin(w)
        y1 = y2 = 0.0
        for i in range(first, n):
            y = a1 * y1 - a2 * y2 + (b * excite[i] if i < m else 0.0)
            out[i] += y
            y2, y1 = y1, y
            if i >= m and -1e-7 < y1 < 1e-7 and -1e-7 < y2 < 1e-7:
                break
    return out


PLATE = (1.0, 1.59, 2.14, 2.30, 2.65, 2.92, 3.16, 3.50)   # a plate's modes (free square plate, approximately)
BAR = (1.0, 2.756, 5.404, 8.933, 13.34)                  # a free bar's
SHELL = (1.0, 1.47, 2.09, 2.56, 2.81, 3.97, 5.06)         # a thick casting's (a breech block, a cradle)


def clank(n, at, seed, f0, ratios=SHELL, decay_s=0.5, hardness=0.6, amp=1.0, spread=0.06):
    """Steel struck at sample `at`: modes at f0 x ratios (detuned a little), higher ones shorter and, when the strike
    is soft, weaker; a contact scrape of noise on top."""
    rng = Rand(seed)
    modes = []
    for k, r in enumerate(ratios):
        f = f0 * r * (1.0 + rng.sym(spread))
        g = (0.55 + 0.45 * rng.uni()) / (1.0 + k * (1.2 - hardness))
        modes.append((f, decay_s * (0.35 + 0.65 / (1.0 + 0.6 * k)) * rng.uni(0.75, 1.15), g))
    x = pulse(n, at, 0.15 + 1.6 * (1.0 - hardness))
    rings = ring(x, modes, n)
    # Contact noise: a few ms of bright scrape.
    w = white(n, seed + 77)
    burst = zeros(n)
    m = ns(0.004 + 0.006 * hardness)
    for k in range(m):
        if at + k < n:
            burst[at + k] = w[at + k] * exp(-5.0 * k / m)
    burst = chain(burst, ("hp", 1500.0 + 2500.0 * hardness))
    return add(scale(rings, amp), scale(burst, 0.35 * amp * hardness))


def thump(n, at, f_start, f_end, decay_s, amp=1.0, seed=1):
    """A heavy low hit: a sine falling from f_start to f_end, decaying over `decay_s` (t60), with a soft click."""
    out = zeros(n)
    ph = 0.0
    k = 6.907755 / max(decay_s, 0.01)
    for i in range(at, n):
        t = (i - at) / RATE
        f = f_end + (f_start - f_end) * exp(-t / max(decay_s * 0.25, 0.01))
        ph += f / RATE
        e = exp(-k * t) * min(1.0, t / 0.0025)
        if e < 1e-5 and t > 0.05:
            break
        out[i] = amp * e * sin(TAU * ph)
    noise = white(n, seed)
    for i in range(at, min(n, at + ns(0.012))):
        out[i] += 0.25 * amp * noise[i] * (1.0 - (i - at) / ns(0.012))
    return out


def burst(n, at, seed, attack, hold, decay_s, lo=None, hi=None, amp=1.0, rough=0.0, period=None):
    """A shaped burst of noise (gas, steam, air): band-limited between lo and hi Hz, with an attack, a hold and an
    exponential decay (t60), optionally rough (a fast random flutter)."""
    w = white(n, seed, period)
    if lo:
        w = chain(w, ("hp", lo), ("hp", lo))
    if hi:
        w = chain(w, ("lp", hi), ("lp", hi))
    e = zeros(n)
    k = 6.907755 / max(decay_s, 0.01)
    a, h = ns(attack), ns(hold)
    for i in range(max(0, at), n):
        j = i - at
        if j < a:
            v = j / max(1, a)
        elif j < a + h:
            v = 1.0
        else:
            v = exp(-k * (j - a - h) / RATE)
            if v < 1e-5:
                break
        e[i] = v * amp
    if rough:
        fl = wander(n, seed + 3, 55.0, period)
        e = [v * (1.0 + rough * f) for v, f in zip(e, fl)]
    return mul(w, e)


# ---------------------------------------------------------------- the cache

def _sources():
    """A hash of the code each top-level function depends on (its own source and, transitively, every function and
    constant of this file it names), so a change remakes exactly the sounds it can change."""
    import ast
    import hashlib
    with open(os.path.abspath(__file__), encoding="utf-8") as f:
        text = f.read()
    lines = text.split("\n")
    defs = {}
    for node in ast.parse(text).body:
        names = []
        if isinstance(node, ast.FunctionDef):
            names = [node.name]
            a = (node.decorator_list[0].lineno if node.decorator_list else node.lineno) - 1
        elif isinstance(node, ast.Assign):
            names = [t.id for t in node.targets if isinstance(t, ast.Name)]
            a = node.lineno - 1
        else:
            continue
        src = "\n".join(lines[a:node.end_lineno])
        used = {n.id for n in ast.walk(node) if isinstance(n, ast.Name)}
        for name in names:
            defs[name] = (src, used)
    hashes = {}
    for name in defs:
        seen, todo = set(), [name]
        while todo:
            x = todo.pop()
            if x in seen or x not in defs:
                continue
            seen.add(x)
            todo.extend(defs[x][1])
        hashes[name] = hashlib.sha256("\n".join(defs[x][0] for x in sorted(seen)).encode()).hexdigest()[:12]
    return hashes


CODE = _sources()


def _path(kind, args, code=""):
    return synth.cached("iw_" + kind, (args, VERSION, code))


def _save(path, chans, top=0.89):
    m = max(peak(c) for c in chans) or 1.0
    synth.write_wav(path, [scale(c, top / m) for c in chans])
    return path


def _arguments(fn, kw):
    """Every argument a call gives `fn`, its defaults included (art/audio/inspect.py hides the standard inspect)."""
    code = fn.__code__
    names = code.co_varnames[:code.co_argcount]
    defaults = fn.__defaults__ or ()
    args = dict(zip(names[len(names) - len(defaults):], defaults))
    unknown = set(kw) - set(names)
    if unknown:
        raise TypeError("%s: no argument %s" % (fn.__name__, ", ".join(sorted(unknown))))
    args.update(kw)
    missing = [a for a in names if a not in args]
    if missing:
        raise TypeError("%s: missing %s" % (fn.__name__, ", ".join(missing)))
    return sorted(args.items())


def made(fn):
    """Caches a generator's result as a WAV under art/.cache/audio/synth: fn(**kw) gives a list of channels; the
    cache's name is the sound's, its key every argument (defaults included), the shared code and the generator's."""
    def wrapper(name, **kw):
        path = _path(name, (fn.__name__, _arguments(fn, kw)), CODE.get(fn.__name__, ""))
        if os.path.isfile(path):
            return path
        return _save(path, fn(**kw))
    wrapper.__name__ = fn.__name__
    wrapper.__doc__ = fn.__doc__
    return wrapper


def looped(path, start, period, overlap, seconds, pitch=1.0, fx=""):
    """A recording made periodic: `period` seconds from `start` (varispeed `pitch`), its end crossfaded into its start
    over `overlap` seconds (equal power), repeated to fill `seconds`; `fx` an ffmpeg filter chain applied first."""
    key = (os.path.basename(path), os.path.getsize(path), start, period, overlap, seconds, pitch, fx)
    out_path = _path("loop_" + os.path.splitext(os.path.basename(path))[0], key, CODE.get("looped", ""))
    if os.path.isfile(out_path):
        return out_path
    rate = int(round(RATE / pitch))
    need = (period + overlap) * pitch + 0.05
    if fx:
        tmp = out_path + ".src.wav"
        audiolib.run(["-ss", "%.4f" % start, "-t", "%.4f" % need, "-i", path, "-af",
                      "aresample=%d,aformat=sample_fmts=flt:channel_layouts=mono,%s" % (rate, fx),
                      "-c:a", "pcm_f32le", tmp])
        x = audiolib.decode(tmp, rate)
        os.remove(tmp)
    else:
        x = audiolib.decode(path, rate, start, need)
    p, o = ns(period), ns(overlap)
    if len(x) < p + o:
        raise SystemExit("%s: too short for a %.1f s loop from %.1f s" % (path, period, start))
    one = array("d", x[:p])
    for i in range(o):
        a = (i + 0.5) / o
        one[i] = x[i] * sin(0.5 * math.pi * a) + x[p + i] * cos(0.5 * math.pi * a)
    n = ns(seconds)
    out = array("d", itertools.islice(itertools.cycle(one), n))
    return _save(out_path, [out])


# ---------------------------------------------------------------- spaces

def steel_ir(name, seconds=1.6, t60=1.1, bright=7500.0, dark=900.0, early=((0.004, 0.5), (0.011, 0.35), (0.019, 0.3),
                                                                            (0.031, 0.2)),
             modes=((38.0, 1.8, 0.5), (61.0, 1.4, 0.35), (97.0, 1.0, 0.3), (146.0, 0.8, 0.2), (213.0, 0.6, 0.15)),
             seed=1):
    """A steel space's impulse response: close reflections off plate, a dense tail that darkens from `bright` to
    `dark` Hz, and the structure's low modes ringing on; its energy is one, so convolving keeps a noise's level."""
    path = _path(name, ("steel_ir", seconds, t60, bright, dark, early, modes, seed), CODE.get("steel_ir", ""))
    if os.path.isfile(path):
        return path
    n = ns(seconds)
    rng = synth.Lcg(seed)
    out = zeros(n)
    k = log(1000.0) / t60
    state = 0.0
    for i in range(n):
        t = i / RATE
        cutoff = bright * (dark / bright) ** min(1.0, t / seconds)
        a = exp(-TAU * cutoff / RATE)
        state = a * state + (1 - a) * rng.next()
        out[i] = state * exp(-k * t) * min(1.0, t / 0.003 + 0.2)
    for at, g in early:
        add_at(out, chain(white(ns(0.006), seed + int(at * 1e4)), ("hp", 600.0)), ns(at), g * 0.6)
    imp = pulse(n, ns(0.002), 0.3)
    add_at(out, ring(imp, modes, n), 0, 0.08)
    e = sqrt(sum(v * v for v in out)) or 1.0
    out = scale(out, 1.0 / e)
    synth.write_wav(path, [out])
    return path


# ---------------------------------------------------------------- contactors, sparks, valves, ticks

def contactor(n, at, seed, size=1.0, arc=1.0, hum=0.0):
    """A contactor closing at sample `at`: the coil's thump, the armature's clack, the contacts' arc and the coil's
    buzz as it pulls in; `size` lowers and slows it (a big contactor), `hum` keeps a little of the buzz."""
    rng = Rand(seed)
    out = thump(n, at, 95.0 / size, 70.0 / size, 0.08 * size, 0.55, seed)
    out = add(out, clank(n, at + ns(0.003), seed + 1, 1180.0 / size, SHELL, 0.11 * size, 0.85, 0.9))
    # The arc: a crackling 'tzk' as the contacts make and bounce.
    w = white(n, seed + 2)
    a = zeros(n)
    length = ns(0.012 + 0.02 * arc * rng.uni())
    for k in range(length):
        i = at + ns(0.004) + k
        if i >= n:
            break
        gate = 1.0 if rng.uni() > 0.35 else 0.15
        a[i] = w[i] * gate * (1.0 - k / length)
    out = add(out, scale(chain(a, ("hp", 1800.0), ("lp", 9000.0)), 0.45 * arc))
    # The coil's buzz while the armature seats: 100 Hz and its odd harmonics, fading to `hum`.
    b = zeros(n)
    for i in range(at, n):
        t = (i - at) / RATE
        e = (0.16 * exp(-t / 0.05) + hum) * min(1.0, t / 0.004)
        if e < 1e-4 and t > 0.3:
            break
        th = TAU * 100.0 * t
        b[i] = e * (sin(th) + 0.45 * sin(3 * th) + 0.22 * sin(5 * th) + 0.12 * sin(7 * th))
    return add(out, b)


def spark(n, at, seed, amp=1.0):
    """An igniter's spark: a dry snap with a little ring."""
    x = pulse(n, at, 0.08)
    out = ring(x, ((4200.0, 0.02, 0.4), (6900.0, 0.012, 0.3), (2600.0, 0.03, 0.2)), n)
    w = white(n, seed)
    for k in range(ns(0.003)):
        if at + k < n:
            out[at + k] += 0.9 * w[at + k] * (1 - k / ns(0.003))
    return scale(chain(out, ("hp", 1200.0)), amp)


def tick(n, at, seed, f0=2600.0, amp=1.0, t60=0.06):
    """Metal ticking as it cools or rattles: a small bright ring."""
    return clank(n, at, seed, f0, PLATE[:5], t60, 0.9, amp, 0.08)


# ---------------------------------------------------------------- the drive

FULL_SHAFT = 85.0     # Hz at full speed (5,100 rpm); ground idle 0.62 (53 Hz), flight idle 0.68, load 0.84 and 1.0
BLADES = 23           # the turbine's first stage: its blade-pass whine (1.2 kHz at ground idle, 2 kHz at full)
STAGE2 = 31           # a second stage, fainter
GEAR = 9              # the accessory gearbox's mesh order
HULL = ((36.0, 7.0, 3.0), (97.0, 9.0, 2.0), (158.0, 10.0, 1.5))   # the hull's low modes: Hz, Q, swell at resonance
PUMP_TOP = 9.5        # a fuel pump's strokes a second at full speed


def resonance(f, f0, q):
    """How a mode at f0 (quality q) answers a force at f: 1 at resonance, falling away either side."""
    if f <= 1.0:
        return 0.0
    x = f / f0 - f0 / f
    return 1.0 / (1.0 + q * q * x * x)


def _rattle(n, seed, rate, amp, period=None, bright=1.0, objects=4):
    """Loose fittings ticking at `rate` (an array or a number, per second): several objects, each with its own
    small ring, so no two neighbouring ticks need sound alike."""
    rng = Rand(seed)
    span = period or n
    top = max(rate) if hasattr(rate, "__len__") else rate
    if top <= 0:
        return zeros(n)
    banks = []
    for k in range(objects):
        f0 = rng.uni(1400.0, 4200.0) * bright
        banks.append((tuple((f0 * r * rng.uni(0.97, 1.03), rng.uni(0.025, 0.08) / (1 + 0.3 * j), 0.8 / (1 + 0.5 * j))
                            for j, r in enumerate(PLATE[:5])), zeros(n)))
    i = 0
    while True:
        i += int(-log(max(1e-9, rng.uni())) * RATE / top) + 1
        if i >= span:
            break
        r = rate[i] if hasattr(rate, "__len__") else rate
        if rng.uni() * top > r:
            continue
        g = rng.uni(0.3, 1.0) ** 2 * (1 if rng.uni() > 0.5 else -1)
        imp = banks[min(objects - 1, int(rng.uni() * objects))][1]
        for rep in range(0, n, span):
            if i + rep < n:
                imp[i + rep] += g
    out = zeros(n)
    for modes, imp in banks:
        out = add(out, ring(imp, modes, n))
    return scale(out, amp)


def _crackle(n, seed, rate, amp, ms=1.2, period=None, hp=1500.0):
    """Combustion's crackle: pops at up to `rate` a second (an array or a number) with heavy-tailed sizes."""
    rng = Rand(seed)
    span = period or n
    top = max(rate) if hasattr(rate, "__len__") else rate
    imp = zeros(n)
    if top <= 0:
        return imp
    i = 0
    while True:
        i += int(-log(max(1e-9, rng.uni())) * RATE / top) + 1
        if i >= span:
            break
        r = rate[i] if hasattr(rate, "__len__") else rate
        if rng.uni() * top > r:
            continue
        g = rng.uni() ** 3 * (1 if rng.uni() > 0.5 else -1)
        for rep in range(0, n, span):
            if i + rep < n:
                imp[i + rep] += g
    return scale(chain(decay(imp, ms), ("hp", hp)), amp)


def _pump(n, speed, seed, period=None, start=0, level=1.0):
    """A fuel pump at `speed` (an array, 0-1): its motor's whine, a gear pump's mesh and the strokes' chug."""
    fm = [24.5 * s for s in speed]                  # the motor's rotation, 1,470 rpm at full
    ph = phase(fm, period, start)
    motor = tones(ph, ((1, 0.5, 0.0), (2, 0.35, 0.1), (4, 0.12, 0.3), (9, 0.4, 0.0), (18, 0.12, 0.2), (27, 0.05, 0.4)))
    g = [s * s for s in speed]
    motor = mul(motor, g)
    fp = [PUMP_TOP * s for s in speed]
    pp = phase(fp, period, start)
    shape = [(0.5 + 0.5 * cos(TAU * p)) ** 6 for p in pp]
    body = chain(white(n, seed + 11, period), ("lp", 240.0), ("hp", 45.0))
    clicks = chain(white(n, seed + 12, period), ("bp", 3100.0, 1.2))
    sharp = [v ** 5 for v in shape]
    chug = add(mul(mul(body, shape), speed), scale(mul(mul(clicks, sharp), speed), 0.25))
    return scale(add(scale(motor, 0.05), scale(chug, 0.5)), level)


def drive_parts(n, speed, load, flame, power, pump, seed, period=None, start=0, whine=1.0, roar=1.0, rough=0.0,
                hull=1.0, rattle=1.0):
    """The drive, sample by sample, from its state, as named parts: speed (0-1.2 of full), load (0-1.5), flame (0-1),
    power (0-1: the electrical hum) and pump (0-1). `rough` adds a strained edge (the boost)."""
    parts = {}
    jit = wander(n, seed + 1, 1.1, period)
    fs = [FULL_SHAFT * s * (1.0 + 0.0035 * j) for s, j in zip(speed, jit)]
    ph = phase(fs, period, start)
    spin = [s ** 1.2 for s in speed]
    # The shaft: its hum and harmonics, swelling a little with load; its first two orders swell further as they pass
    # the hull's modes (the hull answering the drive as it spools through them).
    am = wander(n, seed + 2, 0.7, period)
    g = [0.10 * s * (1.0 + 0.35 * l) * (1.0 + 0.15 * a) for s, l, a in zip(spin, load, am)]
    b1 = [1.0 + hull * sum(G * resonance(f, f0, q) for f0, q, G in HULL) for f in fs]
    b2 = [1.0 + hull * sum(G * resonance(2.0 * f, f0, q) for f0, q, G in HULL) for f in fs]
    o1 = mul(tone(ph, 1), b1)
    o2 = mul(tone(ph, 2, 0.62, 0.13), b2)
    rest = tones(ph, ((3, 0.40, 0.31), (4, 0.33, 0.07), (5, 0.16, 0.5), (6, 0.22, 0.27), (8, 0.10, 0.6),
                      (10, 0.06, 0.81)))
    parts["shaft"] = mul(add(o1, o2, rest), g)
    # The turbine: blade-pass whine with the shaft's sidebands, a second stage, the gearbox's mesh.
    side = [1.0 + 0.3 * sin(TAU * p) for p in ph]
    wj = wander(n, seed + 3, 3.0, period)
    # A compressor under acceleration sings louder: the whine swells while the shaft spools up.
    accel = lowpass1([0.0] + [(b - a) * RATE for a, b in zip(speed, speed[1:])], 3.0)
    gw = [whine * 0.044 * s ** 1.5 * (1.0 + 0.6 * l) * (1.0 + (0.25 + rough) * j) * min(4.0, 1.0 + 10.0 * max(0.0, a))
          for s, l, j, a in zip(speed, load, wj, accel)]
    parts["whine"] = mul(mul(tones(ph, ((BLADES, 1.0, 0.0), (2 * BLADES, 0.22, 0.4), (STAGE2, 0.35, 0.7))), side), gw)
    parts["gear"] = mul(tones(ph, ((GEAR, 1.0, 0.0), (2 * GEAR, 0.3, 0.5))), scale(spin, 0.017))
    # Combustion: a body, a middle that brightens with speed, an upper band and a hiss, all moved by turbulence.
    w = white(n, seed + 4, period)
    body = chain(w, ("lp", 260.0, 0.9), ("hp", 28.0))
    mid = chain(w, ("bp", 560.0, 0.7))
    upper = chain(w, ("bp", 1700.0, 0.8))
    hiss = chain(w, ("hp", 3800.0), ("lp", 14000.0))
    t1 = wander(n, seed + 5, 2.2, period)
    t2 = wander(n, seed + 6, 11.0, period)
    turb = [1.0 + 0.22 * a + 0.10 * b for a, b in zip(t1, t2)]
    lvl = [roar * f * (0.32 + 0.68 * min(l, 1.6)) for f, l in zip(flame, load)]
    rb = [v * 0.9 for v in lvl]
    rm = [v * (0.30 + 0.45 * l) for v, l in zip(lvl, load)]
    ru = [v * (0.08 + 0.30 * l + 0.25 * rough) for v, l in zip(lvl, load)]
    rh = [v * (0.025 + 0.10 * l + 0.08 * rough) for v, l in zip(lvl, load)]
    parts["flame"] = mul(add(mul(body, rb), mul(mid, rm), mul(upper, ru), mul(hiss, rh)), turb)
    crack = _crackle(n, seed + 7, [f * (6.0 + 55.0 * max(0.0, l - 0.3) + 120.0 * rough)
                                   for f, l in zip(flame, load)], 0.35 * roar, 1.0, period)
    parts["crackle"] = mul(crack, [0.4 + 4.5 * max(0.0, l - 0.2) + 3.0 * rough for l in load])
    # The pumps and the electrical hum of everything switched on.
    parts["pumps"] = _pump(n, pump, seed + 8, period, start, 0.75)
    parts["mains"] = mul(tones(ph_line(n, 100.0, period, start), ((1, 1.0, 0.0), (2, 0.5, 0.2), (3, 0.3, 0.4),
                                                                   (5, 0.12, 0.1))), scale(power, 0.008))
    # The hull's own low modes ringing, excited by everything running (the settled resonance).
    hw = white(n, seed + 10, period)
    ex = [hull * (0.5 * s + 0.5 * v) for s, v in zip(spin, lvl)]
    ringing = add(*[scale(chain(hw, ("bp", f0, q * 1.6)), 3.5 / (1 + k)) for k, (f0, q, _) in enumerate(HULL)])
    parts["hull"] = mul(ringing, ex)
    parts["rattle"] = _rattle(n, seed + 9, [0.4 + 3.5 * s * s + 4.0 * l * f + 10.0 * rough for s, l, f in
                                            zip(speed, load, flame)], 0.09 * rattle, period)
    return parts


def drive_sound(n, speed, load, flame, power, pump, seed, period=None, start=0, whine=1.0, roar=1.0, rough=0.0,
                hull=1.0, rattle=1.0):
    """The drive's parts mixed (drive_parts)."""
    return add(*drive_parts(n, speed, load, flame, power, pump, seed, period, start, whine, roar, rough, hull,
                            rattle).values())


def ph_line(n, hz, period=None, start=0):
    """The phase of a fixed frequency (the mains' 100 Hz hum), periodic when asked."""
    return phase(const(n, hz), period, start)


@made
def drive_loop(speed, load, seconds=16.0, pre=4.0, seed=1, whine=1.0, roar=1.0, rough=0.0, hull=1.0, rattle=1.0):
    """The drive running steadily (a loop): `seconds` periodic after `pre` seconds of settling."""
    n = ns(pre + seconds) + 64
    period = ns(seconds)
    start = ns(pre)
    sp, ld = const(n, speed), const(n, load)
    return [drive_sound(n, sp, ld, const(n, 1.0), const(n, 1.0), const(n, min(1.0, 0.75 + 0.25 * speed)), seed, period,
                        start, whine, roar, rough, hull, rattle)]


@made
def drive_start(seconds=7.6, seed=1, contactors=(0.0, 0.34), sparks=(1.15, 1.85), light=1.85, idle=0.62):
    """The drive starting: the main contactor, the pumps' contactor, the pumps running up, the igniter, light-off and
    the spool to ground idle with the hull's resonance swelling as the shaft passes it, then settling."""
    n = ns(seconds)
    c0, c1 = contactors
    power = env(n, [(0.0, 0.0), (c0 + 0.01, 1.0)])
    pump = env(n, [(0.0, 0.0), (c1 + 0.05, 0.0), (c1 + 0.5, 0.55), (c1 + 1.3, 0.9), (c1 + 2.2, idle * 0.25 + 0.75)])
    speed = env(n, [(0.0, 0.0), (c1 + 0.25, 0.0), (light, 0.13), (light + 0.8, 0.22), (light + 2.2, 0.47),
                    (light + 3.3, idle + 0.035), (light + 4.0, idle + 0.01), (seconds, idle)])
    flame = env(n, [(0.0, 0.0), (light - 0.01, 0.0), (light + 0.08, 1.25), (light + 0.6, 0.9), (light + 1.5, 1.0)])
    load = env(n, [(0.0, 0.0), (light, 0.0), (light + 0.15, 0.55), (light + 1.2, 0.3), (light + 2.6, 0.12),
                   (light + 4.0, 0.0)])
    out = drive_sound(n, speed, load, flame, power, pump, seed)
    # Contactors are loud for switchgear but quiet beside a running drive (and the drive must set the level).
    out = add(out, scale(contactor(n, ns(c0), seed + 20, 1.25, 1.0, 0.0), 0.35),
              scale(contactor(n, ns(c1), seed + 21, 0.95, 0.7, 0.0), 0.35))
    t = sparks[0]
    k = 0
    while t < sparks[1]:
        add_at(out, spark(ns(0.05), 0, seed + 30 + k, 0.25), ns(t))
        t += 1.0 / 9.0
        k += 1
    # Light-off: the flame catching in the chamber.
    add_at(out, thump(ns(0.9), 0, 76.0, 42.0, 0.45, 0.5, seed + 40), ns(light))
    add_at(out, burst(ns(0.9), 0, seed + 41, 0.012, 0.03, 0.45, 60.0, 650.0, 0.6, 0.3), ns(light))
    return [out]


@made
def drive_shutdown(seconds=8.6, seed=1, valve=0.0, pumps_off=1.0, mains_off=5.6, idle=0.62, ticks=9):
    """The drive stopping: the fuel valve shuts, the flame dies with a last puff, the turbine runs down through the
    hull's resonance, the pumps' contactor drops and they stop, the main contactor drops, then the metal ticks as it
    cools."""
    n = ns(seconds)
    speed = env(n, [(0.0, idle), (0.4, idle * 0.97)] + [(0.4 + t, idle * 0.97 * exp(-t / 2.3)) for t in
                                                         (0.5, 1.0, 1.6, 2.4, 3.3, 4.4, 5.6, 6.8)] + [(7.6, 0.0)])
    flame = env(n, [(0.0, 1.0), (valve + 0.12, 0.8), (valve + 0.3, 0.25), (valve + 0.75, 0.0)])
    load = env(n, [(0.0, 0.0), (valve + 0.1, 0.25), (valve + 0.4, 0.0)])
    power = env(n, [(0.0, 1.0), (mains_off, 1.0), (mains_off + 0.02, 0.0)])
    pump = env(n, [(0.0, idle * 0.25 + 0.75), (pumps_off, idle * 0.25 + 0.75), (pumps_off + 0.5, 0.45),
                   (pumps_off + 1.2, 0.12), (pumps_off + 1.5, 0.0)])
    out = drive_sound(n, speed, load, flame, power, pump, seed)
    add_at(out, burst(ns(1.0), 0, seed + 41, 0.01, 0.02, 0.5, 50.0, 500.0, 0.35, 0.3), ns(valve + 0.15))
    add_at(out, contactor(ns(0.8), 0, seed + 20, 0.95, 1.4, 0.0), ns(pumps_off), 0.35)
    add_at(out, contactor(ns(0.8), 0, seed + 21, 1.25, 1.6, 0.0), ns(mains_off), 0.35)
    add_at(out, thump(ns(0.5), 0, 85.0, 60.0, 0.2, 0.25, seed + 42), ns(pumps_off + 1.45))
    rng = Rand(seed + 50)
    for k in range(ticks):
        t = mains_off + 0.4 + rng.uni() * (seconds - mains_off - 0.8)
        add_at(out, tick(ns(0.2), 0, seed + 60 + k, rng.uni(2100.0, 4300.0), rng.uni(0.05, 0.16), rng.uni(0.04, 0.1)),
               ns(t))
    return [out]


@made
def drive_release(seconds=2.6, seed=1, top=0.92, idle=0.68, blow=1.0):
    """Thrust falling: the load lets go with an exhale, the blow-off valve vents, the turbine's whine falls back
    towards idle and fades (the loops carry on under it)."""
    n = ns(seconds)
    speed = env(n, [(0.0, top), (0.25, top - 0.05), (0.9, idle + 0.06), (1.8, idle)])
    fade = env(n, [(0.0, 1.0), (0.5, 0.8), (1.4, 0.35), (2.2, 0.0)])
    load = env(n, [(0.0, 0.9), (0.18, 0.4), (0.6, 0.05), (1.2, 0.0)])
    flame = env(n, [(0.0, 1.0), (0.3, 0.7), (1.0, 0.0)])
    zero = const(n, 0.0)
    out = drive_sound(n, speed, load, flame, zero, zero, seed, whine=1.4, hull=0.6)
    out = mul(out, fade)
    vent = burst(n, ns(0.03), seed + 40, 0.006, 0.06, 1.1, 900.0, 7000.0, 0.22 * blow, 0.35)
    vent = add(vent, scale(svf(burst(n, ns(0.03), seed + 41, 0.006, 0.05, 0.9, None, None, 1.0, 0.2),
                                env(n, [(0.0, 3200.0), (1.0, 1400.0)], "exp"), 2.5, "bp"), 0.12 * blow))
    puff = burst(n, 0, seed + 42, 0.02, 0.05, 0.5, 40.0, 420.0, 0.5)
    return [add(out, vent, puff)]


# ---------------------------------------------------------------- lift engines and boost

@made
def lift_loop(speed, seconds=16.0, pre=4.0, seed=1):
    """The four lift engines: ducted fans throbbing as their blade-passes beat, the downwash's roar and the cores'
    hiss (a loop)."""
    n = ns(pre + seconds) + 64
    period, start = ns(seconds), ns(pre)
    out = zeros(n)
    rot = 9.6 + 2.4 * speed                     # fan rotation, Hz
    for k, detune in enumerate((0.0, 0.0062, -0.0045, 0.0027)):
        j = wander(n, seed + 10 + k, 0.6, period)
        f = [rot * (1.0 + detune + 0.002 * v) for v in j]
        ph = phase(f, period, start)
        blade = tones(ph, ((14, 1.0, 0.1 * k), (28, 0.45, 0.3 + 0.1 * k), (42, 0.2, 0.2 * k), (1, 0.25, 0.0),
                           (2, 0.12, 0.5)))
        chop = [0.8 + 0.2 * cos(TAU * p) for p in ph]
        out = add(out, scale(mul(blade, chop), 0.05 * (0.8 + 0.4 * speed)))
    w = white(n, seed + 2, period)
    wash = chain(w, ("lp", 900.0 + 500.0 * speed, 0.7), ("peak", 260.0, 0.9, 5.0), ("hp", 35.0))
    hiss = chain(w, ("hp", 2800.0), ("lp", 12000.0))
    t1 = wander(n, seed + 3, 1.6, period)
    t2 = wander(n, seed + 4, 7.0, period)
    turb = [1.0 + 0.25 * a + 0.12 * b for a, b in zip(t1, t2)]
    out = add(out, mul(add(scale(wash, 0.55), scale(hiss, 0.05 + 0.08 * speed)), turb))
    out = add(out, _rattle(n, seed + 5, 1.0 + 4.0 * speed, 0.02, period))
    return [out]


@made
def boost_loop(level, seconds=12.0, pre=4.0, seed=1):
    """The boost: the drive overdriven, its flame harsh and crackling, its whine rough, and the structure straining:
    a low groan that wavers and a steel resonance singing over the roar (a loop)."""
    n = ns(pre + seconds) + 64
    period, start = ns(seconds), ns(pre)
    sp = const(n, 1.0 + 0.08 * level)
    ld = const(n, 1.15 + 0.3 * level)
    one = const(n, 1.0)
    out = drive_sound(n, sp, ld, one, one, one, seed, period, start, 1.3, 1.25, 0.5 + 0.5 * level, 1.4, 1.8)
    # The groan: a low tone wavering in pitch, roughened by stick-slip.
    gw = wander(n, seed + 20, 0.5, period)
    f = [78.0 + 9.0 * v for v in gw]
    gph = phase(f, period, start)
    groan = tones(gph, ((1, 1.0, 0.0), (2, 0.7, 0.2), (3, 0.5, 0.4), (4, 0.3, 0.1), (5, 0.2, 0.3)))
    slip = wander(n, seed + 21, 22.0, period)
    swell = wander(n, seed + 22, 0.35, period)
    groan = mul(groan, [(0.6 + 0.4 * s) * max(0.0, 0.55 + 0.9 * w) for s, w in zip(slip, swell)])
    groan = chain(groan, ("bp", 260.0, 0.5))
    # The structure singing: a narrow resonance of steel excited by the roar.
    sing = chain(white(n, seed + 23, period), ("bp", 2350.0, 30.0))
    sw = wander(n, seed + 24, 0.8, period)
    sing = mul(sing, [max(0.0, 0.4 + v) for v in sw])
    return [add(out, scale(groan, 0.07 * (0.6 + 0.4 * level)), scale(sing, 0.25 * (0.5 + 0.5 * level)))]


@made
def boost_release(seconds=3.2, seed=1):
    """The boost letting go: a deep exhale of the flame, the vent, the whine falling and the structure relaxing with a
    groan that sinks away."""
    n = ns(seconds)
    speed = env(n, [(0.0, 1.06), (0.3, 1.0), (1.2, 0.86), (2.4, 0.84)])
    load = env(n, [(0.0, 1.4), (0.15, 0.7), (0.7, 0.3), (1.6, 0.1)])
    flame = env(n, [(0.0, 1.0), (0.4, 0.8), (1.6, 0.0)])
    fade = env(n, [(0.0, 1.0), (0.6, 0.75), (1.8, 0.3), (2.8, 0.0)])
    zero = const(n, 0.0)
    out = mul(drive_sound(n, speed, load, flame, zero, zero, seed, whine=1.3, roar=1.2, rough=0.6, hull=1.2), fade)
    vent = burst(n, ns(0.04), seed + 40, 0.005, 0.1, 1.5, 700.0, 7500.0, 0.3, 0.4)
    gf = env(n, [(0.0, 84.0), (1.6, 52.0), (seconds, 46.0)], "exp")
    gph = phase(gf)
    groan = chain(tones(gph, ((1, 1.0, 0.0), (2, 0.6, 0.2), (3, 0.45, 0.4), (4, 0.25, 0.1))), ("bp", 220.0, 0.6))
    groan = mul(groan, env(n, [(0.0, 0.0), (0.25, 1.0), (1.2, 0.6), (2.6, 0.0)]))
    return [add(out, vent, scale(groan, 0.09))]


# ---------------------------------------------------------------- gas, steam and air

@made
def hiss(seconds, seed=1, attack=0.01, hold=0.0, decay_s=0.5, lo=800.0, hi=9000.0, rough=0.25, sweep=None, q=1.2):
    """Gas through a valve: a band of noise with a shape; `sweep` (Hz from, Hz to) moves a resonance through it."""
    n = ns(seconds)
    out = burst(n, 0, seed, attack, hold, decay_s, lo, hi, 1.0, rough)
    if sweep:
        f = env(n, [(0.0, sweep[0]), (attack + hold + decay_s * 0.5, sweep[1])], "exp")
        out = add(scale(out, 0.6), scale(svf(out, f, q, "bp"), 1.4))
    return [out]


@made
def puff(seconds=0.4, seed=1, valve=1.0, size=1.0):
    """A manoeuvring jet's puff: the solenoid valve's snap, a hard burst of gas and its low push."""
    n = ns(seconds)
    rng = Rand(seed)
    out = clank(n, 0, seed, 2300.0 * rng.uni(0.9, 1.1), SHELL, 0.05, 0.9, 0.5 * valve)
    body = burst(n, ns(0.004), seed + 1, 0.003, 0.02 * size, 0.12 + 0.18 * size, 300.0, 11000.0, 1.0, 0.45)
    body = add(body, scale(svf(body, env(n, [(0.0, 5200.0), (seconds, 2200.0)], "exp"), 1.8, "bp"), 0.8))
    # A big ship's jets: the gas's hard push of air under the hiss (a chuff and a thump).
    chuff = burst(n, ns(0.004), seed + 3, 0.004, 0.015 * size, 0.1 + 0.08 * size, 250.0, 1200.0, 1.3, 0.3)
    push = burst(n, ns(0.004), seed + 2, 0.004, 0.01, 0.12 * size, 40.0, 300.0, 2.2)
    return [add(out, body, chuff, push)]


# ---------------------------------------------------------------- servos, chains, ratchets, motors

def _s_curve(n, up, down):
    """A motion's speed: up over `up`, held, down over the last `down` seconds (an eased trapezoid)."""
    out = zeros(n)
    u, d = max(1, ns(up)), max(1, ns(down))
    for i in range(n):
        a = min(1.0, i / u)
        b = min(1.0, (n - 1 - i) / d)
        out[i] = (0.5 - 0.5 * cos(math.pi * a)) * (0.5 - 0.5 * cos(math.pi * b))
    return out


@made
def servo(seconds, seed=1, top_hz=140.0, gear=11, up=0.18, down=0.22, grit=1.0, brake=1.0):
    """An electric servo moving a loader's arm: the motor's whine and the gears' mesh following an eased speed, the
    gearbox's grit, and the brake released at the start and set at the end."""
    n = ns(seconds + 0.12)
    m = ns(seconds)
    sp = _s_curve(m, up, down)
    sp = sp + zeros(n - m)
    hunt = wander(n, seed + 5, 9.0)
    f = [top_hz * s * (1.0 + 0.012 * h) for s, h in zip(sp, hunt)]
    ph = phase(f)
    wob = [0.8 + 0.2 * sin(TAU * p / gear) for p in ph]
    lvl = [s ** 0.7 * w for s, w in zip(sp, wob)]
    whine = mul(tones(ph, ((1, 0.4, 0.0), (2, 0.25, 0.2), (3, 0.1, 0.4), (gear, 0.5, 0.1), (2 * gear, 0.18, 0.3),
                           (3 * gear, 0.06, 0.6))), lvl)
    grind = mul(chain(white(n, seed + 1), ("bp", 900.0, 0.8)), scale(lvl, 0.35 * grit))
    comm = [0.7 + 0.3 * sin(TAU * 6 * p) for p in ph]
    grind = mul(grind, comm)
    out = add(scale(whine, 0.5), grind)
    out = add(out, clank(n, 0, seed + 2, 1650.0, SHELL, 0.05, 0.75, 0.6 * brake))
    out = add(out, clank(n, max(0, m - ns(0.02)), seed + 3, 1420.0, SHELL, 0.07, 0.7, 0.8 * brake))
    out = add(out, thump(n, max(0, m - ns(0.02)), 140.0, 110.0, 0.06, 0.25 * brake, seed + 4))
    return [out]


@made
def chain_run(seconds, seed=1, rate=(40.0, 60.0), size=1.0, motor=1.0, up=0.08, down=0.08, seat=0.0):
    """A chain running over its sprockets (a rammer, a hoist): link after link clinking at a rate rising from rate[0]
    to rate[1], the drive motor under it; `seat`: a heavy stop at the end (the rocket seating)."""
    n = ns(seconds + 0.35)
    m = ns(seconds)
    rng = Rand(seed)
    sp = _s_curve(m, up, down) + zeros(n - m)
    lr = env(n, [(0.0, rate[0]), (seconds, rate[1])])
    # Each link's clink lands on a link plate, a sprocket's tooth or a guide, which ring differently; slack links
    # rattle in between.
    banks = (((2650.0, 0.035, 0.8), (3870.0, 0.03, 0.7), (5210.0, 0.02, 0.5), (7300.0, 0.012, 0.3)),
             ((1720.0, 0.05, 0.8), (2930.0, 0.04, 0.5), (4480.0, 0.025, 0.4)),
             ((3350.0, 0.03, 0.8), (4990.0, 0.02, 0.6), (6620.0, 0.015, 0.4)))
    imps = [zeros(n) for _ in banks]
    acc = 0.0
    for i in range(m):
        acc += lr[i] * sp[i] / RATE
        if acc >= 1.0:
            acc -= 1.0
            j = i + int(rng.uni(0, 0.002) * RATE)
            if j < n:
                imps[0][j] += rng.uni(0.35, 1.0) * sp[i] * (1 if rng.uni() > 0.5 else -1)
                b = 1 if rng.uni() < 0.6 else 2
                k = j + int(rng.uni(0.001, 0.006) * RATE)
                if k < n:
                    imps[b][k] += rng.uni(0.15, 0.6) * sp[i] * (1 if rng.uni() > 0.5 else -1)
    links = zeros(n)
    for bank, imp in zip(banks, imps):
        links = add(links, ring(imp, tuple((f / size * rng.uni(0.97, 1.03), t, g) for f, t, g in bank), n))
    f = [95.0 * s for s in sp]
    ph = phase(f)
    mot = mul(tones(ph, ((1, 0.4, 0.0), (2, 0.3, 0.2), (7, 0.35, 0.1), (14, 0.12, 0.3))), sp)
    rumble = mul(chain(white(n, seed + 1), ("bp", 420.0, 0.7)), sp)
    out = add(links, scale(mot, 0.12 * motor), scale(rumble, 0.25))
    if seat:
        out = add(out, clank(n, m, seed + 2, 310.0, SHELL, 0.35, 0.65, 1.1 * seat))
        out = add(out, thump(n, m, 90.0, 55.0, 0.18, 0.9 * seat, seed + 3))
    return [out]


@made
def ratchet_run(clicks=5, spacing=0.045, seed=1, detent=1.0, solenoid=1.0):
    """A ready rack's drum indexing: a solenoid pulls, the pawl clicks over the ratchet's teeth, the detent drops in."""
    lead = 0.03
    n = ns(lead + clicks * spacing + 0.35)
    rng = Rand(seed)
    out = clank(n, 0, seed, 950.0, SHELL, 0.06, 0.6, 0.6 * solenoid)
    out = add(out, thump(n, 0, 120.0, 90.0, 0.05, 0.25 * solenoid, seed + 1))
    t = lead
    for k in range(clicks):
        sp = spacing * (1.0 + 0.18 * k / max(1, clicks - 1)) * rng.uni(0.92, 1.08)
        t += sp
        out = add(out, clank(n, ns(t), seed + 10 + k, rng.uni(3000.0, 3500.0), PLATE[:5], 0.035, 0.95,
                             rng.uni(0.45, 0.7)))
    t += spacing * 0.8
    out = add(out, clank(n, ns(t), seed + 3, 640.0, SHELL, 0.22, 0.6, 1.0 * detent))
    out = add(out, thump(n, ns(t), 110.0, 75.0, 0.1, 0.5 * detent, seed + 4))
    return [out]


@made
def motor_loop(rot_hz, seconds=8.0, pre=3.0, seed=1, gear=23, poles=4, brush=1.0, ring_rumble=1.0, ticks=2.0):
    """An electric drive running steadily (a turret's traverse, a ramp's power pack): the motor's orders and its
    commutation, a gear's mesh whine with sidebands, a slewing ring's rumble and its balls' ticking (a loop)."""
    n = ns(pre + seconds) + 64
    period, start = ns(seconds), ns(pre)
    j = wander(n, seed + 1, 2.0, period)
    f = [rot_hz * (1.0 + 0.004 * v) for v in j]
    ph = phase(f, period, start)
    mot = tones(ph, ((1, 0.5, 0.0), (2, 0.4, 0.15), (poles, 0.25, 0.3), (2 * poles, 0.12, 0.45), (3, 0.15, 0.6)))
    side = [1.0 + 0.35 * sin(TAU * p) for p in ph]
    mesh = mul(tones(ph, ((gear, 1.0, 0.0), (2 * gear, 0.35, 0.25), (3 * gear, 0.1, 0.5))), side)
    comm = [0.6 + 0.4 * abs(sin(TAU * 6 * p)) for p in ph]
    br = mul(chain(white(n, seed + 2, period), ("hp", 2500.0), ("lp", 9000.0)), comm)
    rum = chain(white(n, seed + 3, period), ("bp", 140.0, 0.8))
    rw = wander(n, seed + 4, 3.0, period)
    rum = mul(rum, [1.0 + 0.3 * v for v in rw])
    out = add(scale(mot, 0.12), scale(mesh, 0.05), scale(br, 0.03 * brush), scale(rum, 0.5 * ring_rumble))
    out = add(out, _rattle(n, seed + 5, ticks, 0.03, period, 0.8))
    return [out]


# ---------------------------------------------------------------- rockets and fire

@made
def rocket_loop(seconds=6.0, pre=2.0, seed=1, body=1.0, crackle=1.0):
    """A rocket motor burning: a hard broadband roar with a heavy middle, a tearing top and its crackle, shaken by
    the flame's instability (a loop)."""
    n = ns(pre + seconds) + 64
    period = ns(seconds)
    w = white(n, seed, period)
    low = chain(w, ("lp", 300.0, 0.9), ("hp", 45.0))
    mid = chain(w, ("bp", 700.0, 0.7))
    upper = chain(w, ("bp", 2000.0, 0.9))
    top = chain(w, ("hp", 3500.0), ("lp", 9000.0))
    t1 = wander(n, seed + 1, 9.0, period)
    t2 = wander(n, seed + 2, 38.0, period)
    turb = [max(0.1, 1.0 + 0.2 * a + 0.28 * b) for a, b in zip(t1, t2)]
    roar = mul(add(scale(low, 1.0 * body), scale(mid, 0.9), scale(upper, 0.3), scale(top, 0.07)), turb)
    cr = chain(_crackle(n, seed + 3, 160.0, 0.7 * crackle, 0.7, period, 1500.0), ("lp", 9000.0))
    return [add(roar, cr)]


@made
def rocket_ignite(seconds=1.1, seed=1, size=1.0):
    """A rocket motor lighting: the pop of the charge catching, a short flare and the roar settling in."""
    n = ns(seconds)
    pop = add(thump(n, 0, 110.0, 55.0, 0.2, 0.9, seed), burst(n, 0, seed + 1, 0.002, 0.008, 0.08, 300.0, 4000.0, 0.8))
    w = white(n, seed + 2)
    roar = add(chain(w, ("lp", 300.0), ("hp", 45.0)), scale(chain(w, ("bp", 750.0, 0.7)), 0.9),
               scale(chain(w, ("bp", 2000.0, 0.9)), 0.3), scale(chain(w, ("hp", 3500.0), ("lp", 9000.0)), 0.08))
    e = env(n, [(0.0, 0.0), (0.012, 1.4), (0.08, 0.8), (0.4, 0.55), (seconds - 0.25, 0.4), (seconds, 0.0)])
    roar = mul(roar, e)
    cr = mul(chain(_crackle(n, seed + 3, 180.0, 0.7, 0.7, None, 1500.0), ("lp", 9000.0)), e)
    return [add(scale(pop, size), roar, cr)]


@made
def fire_roar(seconds=24.0, pre=3.0, seed=1, low=320.0, flicker=1.0):
    """A large fire's weight: a low turbulent roar breathing slowly (a loop)."""
    n = ns(pre + seconds) + 64
    period = ns(seconds)
    w = white(n, seed, period)
    body = chain(w, ("lp", low, 0.8), ("lp", low * 1.5), ("hp", 30.0))
    t1 = wander(n, seed + 1, 0.9, period)
    t2 = wander(n, seed + 2, 4.5, period)
    return [mul(body, [max(0.05, 1.0 + 0.45 * a + 0.2 * flicker * b) for a, b in zip(t1, t2)])]


@made
def whump(seconds=1.2, seed=1, size=1.0):
    """Fuel catching all at once: a soft, heavy push of air (the 'whoomp') with the flame's first roar in it."""
    n = ns(seconds)
    out = thump(n, 0, 66.0 / size ** 0.3, 40.0, 0.55 * size, 1.0, seed)
    air = burst(n, 0, seed + 1, 0.035, 0.06, 0.6 * size, 45.0, 700.0, 1.2, 0.25)
    flare = burst(n, ns(0.02), seed + 2, 0.06, 0.1, 0.7 * size, 500.0, 6000.0, 0.35, 0.4)
    return [add(out, air, flare)]


# ---------------------------------------------------------------- alarms

@made
def klaxon(blasts=((0.0, 0.95), (1.3, 0.95)), seconds=2.6, seed=1, low=290.0, high=470.0, diaphragm=430.0,
           horn=(1080.0, 2350.0), rasp=1.0):
    """An electro-mechanical klaxon: a motor spins a toothed wheel against a steel diaphragm, so each blast's pitch
    rises as the motor runs up and falls as it lets go; the diaphragm and the horn's flare shape the rasp."""
    n = ns(seconds)
    rng = Rand(seed)
    f = zeros(n)
    amp = zeros(n)
    for t0, d in blasts:
        i0, i1 = ns(t0), ns(t0 + d)
        for i in range(i0, min(n, i1 + ns(0.25))):
            t = (i - i0) / RATE
            if t <= d:
                ff = low + (high - low) * (1.0 - exp(-t / 0.12))
                a = min(1.0, t / 0.03)
            else:
                r = t - d
                ff = high - (high - low * 0.75) * min(1.0, r / 0.22)
                a = exp(-r / 0.05)
            f[i] = ff
            amp[i] = max(amp[i], a)
    ph = phase(f)
    # The wheel's teeth hitting the diaphragm: a sharp pulse every cycle, its timing a little rough.
    src = zeros(n)
    last = 0.0
    jit = wander(n, seed + 1, 40.0)
    for i in range(1, n):
        p = ph[i] + 0.03 * jit[i]
        if int(p) != int(last) and amp[i] > 0.001:
            src[i] = amp[i] * rng.uni(0.8, 1.0)
        last = p
    src = decay(src, 0.25)
    body = add(ring(src, ((diaphragm, 0.03, 1.0), (diaphragm * 2.03, 0.02, 0.6), (diaphragm * 3.1, 0.015, 0.35)), n),
               scale(chain(src, ("bp", horn[0], 2.5)), 6.0), scale(chain(src, ("bp", horn[1], 3.0)), 4.0),
               scale(chain(src, ("hp", 3000.0)), 1.2 * rasp))
    return [soft(body, 1.6 * rasp)]


@made
def bell(seconds=2.4, seed=1, strike_hz=19.0, f0=1150.0, ring_out=0.9):
    """An electric alarm bell: a trembler's hammer striking a steel gong some twenty times a second, the gong's
    inharmonic ring building under it, then ringing out."""
    n = ns(seconds + ring_out)
    rng = Rand(seed)
    imp = zeros(n)
    t = 0.0
    while t < seconds:
        i = ns(t)
        imp[i] += rng.uni(0.75, 1.0)
        t += (1.0 / strike_hz) * rng.uni(0.94, 1.06)
    exc = decay(imp, 0.08)
    base = ((f0, 1.6, 1.0), (f0 * 2.32, 1.0, 0.7), (f0 * 2.95, 0.8, 0.5), (f0 * 4.18, 0.5, 0.45),
            (f0 * 5.43, 0.35, 0.35), (f0 * 6.8, 0.25, 0.2), (f0 * 0.5, 1.2, 0.15))
    modes = tuple(m for f, t, g in base for m in ((f, t, g), (f * 1.0045, t * 0.9, g * 0.7)))
    gong = ring(exc, modes, n)
    clicks = scale(chain(exc, ("hp", 4000.0)), 0.5)
    hum = zeros(n)
    for i in range(ns(seconds)):
        hum[i] = 0.02 * sin(TAU * 50.0 * i / RATE) * (1.0 + 0.5 * sin(TAU * strike_hz * i / RATE))
    return [add(gong, clicks, hum)]


# ---------------------------------------------------------------- beds inside the ship

@made
def ventilation(seconds=48.0, pre=4.0, seed=1):
    """The ventilation inside (a stereo bed): two fans a little apart in speed so their blade-passes beat slowly,
    air rushing through ducts that colour it, and the grilles' hiss."""
    n = ns(pre + seconds) + 64
    period, start = ns(seconds), ns(pre)
    chans = []
    fans = []
    for k, rot in enumerate((16.0, 16.2)):
        j = wander(n, seed + 10 + k, 0.8, period)
        f = [rot * (1.0 + 0.0015 * v) for v in j]
        ph = phase(f, period, start)
        fans.append(tones(ph, ((7, 1.0, 0.0), (14, 0.45, 0.2), (21, 0.2, 0.4), (1, 0.15, 0.0), (2, 0.08, 0.3))))
    for ch in range(2):
        w = white(n, seed + 20 + ch, period)
        duct = chain(w, ("hp", 110.0), ("lp", 2600.0, 0.6), ("peak", 330.0, 1.2, 4.0), ("peak", 990.0, 2.0, 3.0))
        grille = chain(w, ("hp", 3200.0), ("lp", 9000.0))
        t1 = wander(n, seed + 30 + ch, 0.25, period)
        breath = [1.0 + 0.08 * v for v in t1]
        air = mul(add(scale(duct, 0.5), scale(grille, 0.04)), breath)
        mix = add(air, scale(fans[ch], 0.07), scale(fans[1 - ch], 0.03))
        chans.append(mix)
    return chans


@made
def machinery(seconds=56.0, pre=4.0, seed=1, knocks=9):
    """The machinery inside (a stereo bed): the mains' hum, a generator turning far off, a circulating pump, the
    structure's low rumble, and now and then a pipe's knock or a relay somewhere in the hull."""
    n = ns(pre + seconds) + 64
    period, start = ns(seconds), ns(pre)
    rng = Rand(seed)
    hum_ph = ph_line(n, 100.0, period, start)
    hum = tones(hum_ph, ((1, 0.5, 0.0), (2, 1.0, 0.2), (3, 0.6, 0.4), (4, 0.25, 0.1), (6, 0.12, 0.3)))
    gj = wander(n, seed + 1, 0.4, period)
    gen_ph = phase([47.0 * (1.0 + 0.003 * v) for v in gj], period, start)
    gen = tones(gen_ph, ((1, 1.0, 0.0), (2, 0.6, 0.2), (3, 0.3, 0.5), (4, 0.2, 0.3)))
    pj = wander(n, seed + 2, 1.0, period)
    pump_ph = phase([24.0 * (1.0 + 0.002 * v) for v in pj], period, start)
    pump = tones(pump_ph, ((9, 1.0, 0.0), (18, 0.3, 0.3), (1, 0.4, 0.0)))
    pulse_ph = phase(const(n, 1.4), period, start)
    pump = mul(pump, [0.75 + 0.25 * cos(TAU * p) for p in pulse_ph])
    chans = []
    events = [(rng.uni(0.0, seconds), rng.uni(), rng.uni(0.4, 1.0), rng.uni()) for _ in range(knocks)]
    parts = []
    for k, (t, kind, g, pan) in enumerate(events):
        m = ns(0.6)
        if kind < 0.5:
            part = clank(m, 0, seed + 100 + k, rng.uni(240.0, 420.0), SHELL, 0.4, 0.5, 1.0)
        elif kind < 0.8:
            part = tick(m, 0, seed + 100 + k, rng.uni(1800.0, 3200.0), 0.5, 0.1)
        else:
            part = contactor(m, 0, seed + 100 + k, 0.7, 0.3, 0.0)
        part = chain(part, ("lp", 2400.0))
        # Heard through the hull: a short steel-room tail after it.
        part = add(part, scale(burst(m, ns(0.004), seed + 200 + k, 0.01, 0.0, 0.45, 200.0, 2500.0), 0.25 * peak(part)))
        parts.append(part)
    for ch in range(2):
        w = white(n, seed + 10 + ch, period)
        rumble = chain(w, ("lp", 90.0, 0.8), ("hp", 28.0))
        air = chain(w, ("bp", 600.0, 0.5))
        t1 = wander(n, seed + 20 + ch, 0.3, period)
        rumble = mul(rumble, [1.0 + 0.25 * v for v in t1])
        mix = add(scale(rumble, 0.5), scale(air, 0.05), scale(hum, 0.018), scale(gen, 0.03 + 0.008 * ch),
                  scale(pump, 0.014 if ch == 0 else 0.02))
        for (t, kind, g, pan), part in zip(events, parts):
            side = (0.4 + 0.6 * pan) if ch == 0 else (1.0 - 0.6 * pan)
            at = start + ns(t)
            for rep in (at - period, at, at + period):
                add_at(mix, part, rep, g * side * 0.09)
        chans.append(mix)
    return chans


# ---------------------------------------------------------------- the hull and strain

@made
def groan(seconds=1.6, seed=1, f0=70.0, glide=0.85, rough=0.6, bright=1.0):
    """Steel under strain: friction sticking and slipping drives a low, wavering tone through the plates' resonances
    (a stick-slip model)."""
    n = ns(seconds)
    rng = Rand(seed)
    f = env(n, [(0.0, f0), (seconds * 0.5, f0 * (1 + (glide - 1) * 0.4)), (seconds, f0 * glide)], "exp")
    j = wander(n, seed + 1, 6.0)
    f = [v * (1.0 + 0.04 * u) for v, u in zip(f, j)]
    ph = phase(f)
    src = zeros(n)
    last = 0.0
    slip = wander(n, seed + 2, 30.0)
    for i in range(1, n):
        if int(ph[i]) != int(last):
            src[i] = 1.0 + rough * slip[i]
        last = ph[i]
    e = env(n, [(0.0, 0.0), (seconds * rng.uni(0.15, 0.3), 1.0), (seconds * 0.7, 0.7), (seconds, 0.0)])
    src = mul(decay(src, 1.5), e)
    plates = ring(src, ((f0 * 2.1 * bright, 0.25, 0.6), (f0 * 3.7 * bright, 0.2, 0.5), (f0 * 5.9 * bright, 0.15, 0.4),
                        (f0 * 8.3 * bright, 0.1, 0.3), (f0 * 12.7 * bright, 0.07, 0.2)), n)
    return [add(scale(chain(src, ("lp", 900.0)), 0.3), plates)]


@made
def oilcan(seed=1, f0=180.0, size=1.0):
    """A steel panel popping through as the hull flexes (oil-canning): a snap and a short, low ring."""
    n = ns(0.9 * size)
    out = clank(n, 0, seed, f0, PLATE, 0.45 * size, 0.55, 1.0, 0.04)
    out = add(out, thump(n, 0, f0 * 0.6, f0 * 0.45, 0.25 * size, 0.6, seed + 1))
    return [out]


@made
def strike(seconds=0.6, seed=1, f0=400.0, ratios=SHELL, decay_s=0.4, hardness=0.6, spread=0.06):
    """One steel strike (a clank, a ring, a ping), to be layered at a time of its own."""
    return [clank(ns(seconds), 0, seed, f0, ratios, decay_s, hardness, 1.0, spread)]


@made
def boom(seconds=0.8, seed=1, f_start=70.0, f_end=40.0, decay_s=0.5):
    """A heavy low hit (a falling sine with a click), to be layered."""
    return [thump(ns(seconds), 0, f_start, f_end, decay_s, 1.0, seed)]


@made
def rattle_burst(seconds=0.9, seed=1, rate=45.0, f0=1.0, amp=1.0, decay_s=0.6):
    """Loose things shaken by a blow: fittings, lamp housings, chains on their hooks, rattling and dying away."""
    n = ns(seconds)
    rate_env = env(n, [(0.0, 0.0), (0.02, rate), (decay_s, rate * 0.25), (seconds, 0.0)])
    out = _rattle(n, seed, rate_env, 1.0, None, f0)
    buzz = chain(white(n, seed + 1), ("bp", 1100.0 * f0, 1.5))
    am = [0.5 + 0.5 * sin(TAU * 43.0 * i / RATE) for i in range(n)]
    buzz = mul(mul(buzz, am), env(n, [(0.0, 0.0), (0.01, 0.4), (decay_s * 0.6, 0.1), (seconds, 0.0)]))
    return [scale(add(out, buzz), amp)]


# ---------------------------------------------------------------- the motorboat's outboard (docs/MOTORBOAT.md §7)

def outboard_parts(n, revs, load, seed, period=None, start=0, choke=0.0):
    """A small twin two-stroke outboard, sample by sample: `revs` the crank in revolutions a second, `load` 0-1 (the
    throttle against the water). Each cylinder fires once a revolution: a pulse that rings the exhaust's passages (the
    buzz), its gas bubbling out under the water through the hub (the burble), the crank's orders and the gears' whine,
    the reed valves' chatter, and now and then a firing that misses (more at idle, `choke` more still)."""
    parts = {}
    jit = wander(n, seed + 1, 2.3, period)
    fire = [2.0 * r * (1.0 + 0.012 * j) for r, j in zip(revs, jit)]
    ph = phase(fire, period, start)
    # The pulses: one at each whole cycle of the firing's phase, its strength jittering (periodic: the firing's
    # count in a period repeats its numbers).
    cycles = max(1, round(ph[start + period] - ph[start])) if period and start + period < n else 0
    rng = Rand(seed + 2)
    table = [(rng.uni(0.8, 1.15), rng.uni()) for _ in range(max(cycles, 1) if cycles else 4096)]
    pulses = zeros(n)
    width = max(1, ns(0.0012))
    last = int(ph[0])
    for i in range(1, n):
        k = int(ph[i])
        if k != last:
            last = k
            amp, chance = table[k % len(table)]
            miss = 0.05 + 0.25 * max(0.0, 1.0 - revs[i] / 25.0) + choke
            if chance < miss:
                amp *= 0.15
            amp *= 0.55 + 0.6 * min(1.0, load[i] + 0.15)
            for j in range(width):
                if i + j < n:
                    pulses[i + j] += amp * sin(math.pi * (j + 0.5) / width)
    bright = [0.4 + 0.8 * l for l in load]
    parts["buzz"] = add(chain(pulses, ("bp", 190.0, 1.3)), mul(chain(pulses, ("bp", 460.0, 1.6)), scale(bright, 0.6)),
                        mul(chain(pulses, ("bp", 1150.0, 2.2)), scale(bright, 0.35)))
    # The burble: the exhaust's gas under the water, a low noise each firing opens, bubbles breaking at the surface.
    w = white(n, seed + 3, period)
    gate = decay(pulses, 18.0)
    low = chain(w, ("lp", 320.0, 0.8), ("hp", 35.0))
    parts["burble"] = mul(mul(low, gate), scale([0.6 + 0.6 * l for l in load], 0.9))
    pops = chain(w, ("bp", 900.0, 3.0))
    parts["bubbles"] = mul(mul(pops, decay(pulses, 6.0)), scale([0.2 + 0.3 * l for l in load], 1.0))
    # The crank and the lower unit: its orders, the gears' mesh (13 teeth on the pinion), swelling with speed.
    crank = phase([r * (1.0 + 0.012 * j) for r, j in zip(revs, jit)], period, start)
    spin = [min(1.6, r / 50.0) for r in revs]
    parts["crank"] = mul(tones(crank, ((1, 0.5, 0.0), (2, 1.0, 0.2), (3, 0.35, 0.4), (4, 0.4, 0.1), (6, 0.15, 0.7))),
                         scale(spin, 0.05))
    parts["gears"] = mul(tones(crank, ((13, 1.0, 0.0), (26, 0.25, 0.5))), scale([s * s for s in spin], 0.012))
    # The reeds and the cowl rattling, faster with speed.
    parts["rattle"] = _rattle(n, seed + 4, [3.0 + 22.0 * s + 15.0 * l for s, l in zip(spin, load)], 0.05, period)
    # The intake's hiss under load.
    parts["intake"] = mul(chain(white(n, seed + 5, period), ("bp", 2600.0, 0.9)), [0.004 + 0.02 * l * s for l, s in zip(load, spin)])
    return parts


def outboard_sound(n, revs, load, seed, period=None, start=0, choke=0.0):
    return add(*outboard_parts(n, revs, load, seed, period, start, choke).values())


@made
def outboard_loop(revs, load, seconds=8.0, pre=2.0, seed=1):
    """The outboard running steadily (a loop): `seconds` periodic after `pre` seconds of settling."""
    n = ns(pre + seconds) + 64
    return [outboard_sound(n, const(n, revs), const(n, load), seed, ns(seconds), ns(pre))]


@made
def outboard_start(seconds=2.4, seed=1, pulls=2, idle=15.0):
    """A pull start: the cord's ratchet and the crank turned over (a pull that doesn't catch, then one that does), the
    first firings sputtering as it catches, a little run-up and the idle."""
    n = ns(seconds)
    rng = Rand(seed + 10)
    points = [(0.0, 0.0)]
    t = 0.05
    for k in range(pulls):
        last = k == pulls - 1
        points += [(t, 0.0), (t + 0.05, 9.0), (t + 0.35, 11.0), (t + 0.55, 2.0 if not last else 12.0)]
        t += 0.75
    catch = t - 0.2
    points += [(catch + 0.25, idle * 1.45), (catch + 0.7, idle * 1.1), (seconds, idle)]
    revs = env(n, points)
    load = env(n, [(0.0, 0.0), (catch, 0.1), (catch + 0.3, 0.35), (catch + 0.9, 0.0)])
    out = outboard_sound(n, revs, load, seed, choke=0.0)
    # Before it catches, no firing: the crank alone (the cord's whirr and the compression's thumps).
    mute = env(n, [(0.0, 0.08), (catch - 0.02, 0.08), (catch + 0.05, 1.0)])
    out = mul(out, mute)
    t = 0.05
    for k in range(pulls):
        cord = burst(ns(0.6), 0, seed + 20 + k, 0.01, 0.25, 0.25, 700.0, 4500.0, 0.25, 0.8)
        add_at(out, cord, ns(t))
        for j in range(5):
            add_at(out, thump(ns(0.12), 0, 95.0, 70.0, 0.06, 0.18 * rng.uni(0.7, 1.0), seed + 30 + j), ns(t + 0.06 + 0.09 * j))
        t += 0.75
    return [out]


@made
def outboard_stop(seconds=1.6, seed=1, idle=15.0):
    """The kill switch: the firing stops at once, the crank runs down with a last cough or two, the leg's water gurgles."""
    n = ns(seconds)
    revs = env(n, [(0.0, idle), (0.05, idle * 0.9), (0.6, idle * 0.3), (1.0, 0.0)])
    firing = env(n, [(0.0, 1.0), (0.04, 0.2), (0.3, 0.05), (0.5, 0.0)])
    out = mul(outboard_sound(n, revs, const(n, 0.0), seed, choke=0.3), firing)
    add_at(out, burst(ns(0.5), 0, seed + 40, 0.004, 0.02, 0.18, 60.0, 600.0, 0.6, 0.2), ns(0.12))
    add_at(out, burst(ns(0.5), 0, seed + 41, 0.004, 0.02, 0.15, 60.0, 500.0, 0.4, 0.2), ns(0.42))
    gurgle = mul(chain(white(n, seed + 42), ("lp", 400.0, 0.9)), env(n, [(0.0, 0.0), (0.2, 0.25), (1.2, 0.0)]))
    return [add(out, gurgle)]


if __name__ == "__main__":
    import time
    t = time.time()
    print(drive_loop("test_idle", speed=0.62, load=0.0, seconds=4.0, pre=1.0), "%.1f s" % (time.time() - t))
