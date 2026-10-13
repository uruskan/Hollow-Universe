"""The animals' voices (GROUND.md §6.9, §4.9), made in code: deer, boar, wolf and bear.

No recording of these animals is in the project's libraries, so their calls, alarms, threats, attacks, hurts and
deaths are synthesised the way voices are made: a glottal source (a band-limited sawtooth whose pitch follows a contour,
with per-period jitter and shimmer, period doubling for a roar's roughness, and breath noise) through a few formant
resonators (the vocal tract), with an envelope, an optional rattle (a growl's pulsing) and soft saturation. Clicks make
teeth snapping shut and a bear's jaw popping. The numbers come from what is known of each animal's voice: a wolf howls
at a few hundred hertz, almost a pure tone; growls are low, noisy and pulsing; a pig squeals harshly above a
kilohertz; a deer's alarm is a short, breathy bark. They are stand-ins until recordings are chosen (GROUND.md §11).

Deterministic: the same arguments give the same samples (synth.Lcg), cached under art/.cache/audio/synth.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import synth  # noqa: E402

RATE = synth.RATE
VERSION = 1


def _curve(points, t):
    """Piecewise linear through (t, value) points."""
    if t <= points[0][0]:
        return points[0][1]
    for (t0, v0), (t1, v1) in zip(points, points[1:]):
        if t <= t1:
            return v1 if t1 <= t0 else v0 + (v1 - v0) * (t - t0) / (t1 - t0)
    return points[-1][1]


def _normalise(out, peak=0.89):
    m = max(1e-9, max(abs(x) for x in out))
    k = peak / m
    return [x * k for x in out]


def voice(name, seconds, f0, amp, seed=1, jitter=0.01, shimmer=0.05, breath=0.08, formants=((700, 140, 1.0),),
          rattle=None, sub=0.0, drive=0.0, tone=1.0, lowpass=None):
    """A voice: `f0` and `amp` are (seconds, value) contours; `formants` (frequency or contour, bandwidth, gain);
    `rattle` (rate Hz, depth) pulses the amplitude; `sub` weakens every other period (a subharmonic: roughness);
    `drive` saturates; `tone` is the voiced part against `breath` (noise); `lowpass` darkens the source (Hz)."""
    args = (seconds, tuple(f0), tuple(amp), seed, jitter, shimmer, breath,
            tuple((tuple(f) if isinstance(f, (list, tuple)) else f, b, g) for f, b, g in formants),
            rattle, sub, drive, tone, lowpass, VERSION)
    path = synth.cached("animal_" + name, args)
    if os.path.isfile(path):
        return path
    n = int(seconds * RATE)
    rng = synth.Lcg(seed)
    out = [0.0] * n
    log_f0 = [(t, math.log(f)) for t, f in f0]
    nf = len(formants)
    states = [[0.0, 0.0, 0.0, 0.0] for _ in range(nf)]
    coefs = [(0.0, 0.0, 0.0, 0.0)] * nf
    phase = 0.0
    period_rate = 1.0
    period_amp = 1.0
    odd = False
    lp = 0.0
    lp_a = math.exp(-2 * math.pi * lowpass / RATE) if lowpass else 0.0
    rat_phase = 0.0
    rat_rate = rattle[0] if rattle else 0.0
    block = 64
    for start in range(0, n, block):
        t = start / RATE
        hz = math.exp(_curve(log_f0, t))
        a = _curve(amp, t)
        for i, (f, bw, g) in enumerate(formants):
            fr = _curve(f, t) if isinstance(f, (list, tuple)) else f
            fr = min(fr, RATE * 0.45)
            w = 2 * math.pi * fr / RATE
            alpha = math.sin(w) * bw / (2 * fr)
            a0 = 1 + alpha
            coefs[i] = (g * alpha / a0, -2 * math.cos(w) / a0, (1 - alpha) / a0, 0.0)
        dt = hz / RATE
        for j in range(start, min(n, start + block)):
            d = dt * period_rate
            phase += d
            if phase >= 1.0:
                phase -= 1.0
                period_rate = 1.0 + jitter * rng.next()
                period_amp = 1.0 + shimmer * rng.next()
                odd = not odd
                if rattle:
                    rat_rate = rattle[0] * (1.0 + 0.25 * rng.next())
            v = 2.0 * phase - 1.0
            if phase < d:
                x = phase / d
                v -= x + x - x * x - 1.0
            elif phase > 1.0 - d:
                x = (phase - 1.0) / d
                v -= x * x + x + x + 1.0
            src = v * period_amp * tone * ((1.0 - sub) if odd else 1.0)
            if lowpass:
                lp = lp_a * lp + (1 - lp_a) * src
                src = lp * 3.0
            src += breath * rng.next()
            y = 0.0
            for i in range(nf):
                b0, a1, a2, _ = coefs[i]
                s = states[i]
                yy = b0 * (src - s[1]) - a1 * s[2] - a2 * s[3]
                s[1] = s[0]
                s[0] = src
                s[3] = s[2]
                s[2] = yy
                y += yy
            if rattle:
                rat_phase += rat_rate / RATE
                m = 0.5 + 0.5 * math.sin(2 * math.pi * rat_phase)
                y *= 1.0 - rattle[1] + rattle[1] * m * m
            out[j] = y * a
    out = _normalise(out)
    if drive > 0:
        k = math.tanh(drive)
        out = _normalise([math.tanh(drive * x) / k for x in out])
    synth.write_wav(path, [out])
    return path


def click(name, freq, decay, seconds=0.12, noise=0.4, seed=1, at=0.004):
    """A hard little transient (teeth snapping shut, a jaw's pop, a hoof's tock): a decaying ring and a scratch."""
    path = synth.cached("animal_" + name, (freq, decay, seconds, noise, seed, at, VERSION))
    if os.path.isfile(path):
        return path
    n = int(seconds * RATE)
    rng = synth.Lcg(seed)
    out = [0.0] * n
    i0 = int(at * RATE)
    for i in range(i0, n):
        t = (i - i0) / RATE
        env = math.exp(-t / decay)
        out[i] = env * ((1 - noise) * math.sin(2 * math.pi * freq * t) + noise * rng.next())
    synth.write_wav(path, [_normalise(out)])
    return path


def _r(seed):
    """A variant's own random numbers in [0, 1)."""
    g = synth.Lcg(seed)
    return lambda: 0.5 + 0.5 * g.next()


# ---------------------------------------------------------------- the species
# Each function gives {role: [wav paths]} for one species: call, alarm, threat, attack, hurt, death.

def deer():
    roles = {}
    calls = []
    for k in range(4):
        r = _r(1100 + k)
        f = 300 + 140 * r()
        d = 0.34 + 0.16 * r()
        calls.append(voice("deer_call%d" % k, d + 0.2, [(0, f * 0.9), (0.05, f * 1.06), (d * 0.6, f), (d, f * 0.8)],
                           [(0, 0), (0.03, 1), (d * 0.7, 0.75), (d, 0), (d + 0.2, 0)], seed=110 + k,
                           jitter=0.012, shimmer=0.06, breath=0.1,
                           formants=((620, 150, 1.0), (1650, 210, 0.55), (2850, 320, 0.25), (250, 90, 0.35))))
    roles["call"] = calls
    # The alarm: a short breathy bark, a nasal snort before it.
    alarms = []
    for k in range(3):
        r = _r(1200 + k)
        f = 170 + 60 * r()
        d = 0.16 + 0.08 * r()
        alarms.append(voice("deer_alarm%d" % k, d + 0.25, [(0, f * 1.15), (d, f * 0.8)],
                            [(0, 0), (0.008, 1), (d * 0.5, 0.6), (d, 0), (d + 0.25, 0)], seed=120 + k,
                            jitter=0.05, shimmer=0.2, breath=0.9, tone=0.55,
                            formants=((520, 220, 1.0), (1150, 300, 0.7), (2400, 420, 0.4)), drive=1.2))
    roles["alarm"] = alarms
    hurts = []
    for k in range(3):
        r = _r(1300 + k)
        f = 520 + 200 * r()
        d = 0.42 + 0.2 * r()
        hurts.append(voice("deer_hurt%d" % k, d + 0.2, [(0, f * 0.9), (0.06, f * 1.25), (d * 0.5, f * 1.1), (d, f * 0.7)],
                           [(0, 0), (0.02, 1), (d * 0.6, 0.85), (d, 0), (d + 0.2, 0)], seed=130 + k,
                           jitter=0.03, shimmer=0.12, breath=0.25, sub=0.15,
                           formants=((800, 180, 1.0), (2000, 260, 0.6), (3200, 380, 0.3)), drive=0.8))
    roles["hurt"] = hurts
    deaths = []
    for k in range(2):
        r = _r(1400 + k)
        f = 480 + 120 * r()
        d = 0.8 + 0.25 * r()
        deaths.append(voice("deer_death%d" % k, d + 0.3, [(0, f), (0.1, f * 1.15), (d, f * 0.5)],
                            [(0, 0), (0.03, 1), (d * 0.5, 0.7), (d, 0), (d + 0.3, 0)], seed=140 + k,
                            jitter=0.04, shimmer=0.15, breath=0.3, sub=0.2,
                            formants=((750, 180, 1.0), (1850, 260, 0.55), (3000, 380, 0.25)), drive=0.6))
    roles["death"] = deaths
    return roles


def boar():
    roles = {}

    def grunt(name, seed, f, d, rough=0.06, breath=0.35):
        return voice(name, d + 0.12, [(0, f * 1.1), (d * 0.3, f), (d, f * 0.8)],
                     [(0, 0), (0.015, 1), (d * 0.5, 0.8), (d, 0), (d + 0.12, 0)], seed=seed,
                     jitter=rough, shimmer=0.25, breath=breath, sub=0.25,
                     formants=((330, 110, 1.0), (1050, 200, 0.6), (2300, 300, 0.25), (180, 60, 0.4)), drive=1.5)
    calls = []
    for k in range(4):
        r = _r(2100 + k)
        calls.append(grunt("boar_call%d" % k, 210 + k, 85 + 45 * r(), 0.14 + 0.12 * r()))
    roles["call"] = calls
    # A snort: a burst of breath through the snout.
    roles["alarm"] = [voice("boar_alarm%d" % k, 0.45, [(0, 120), (0.3, 100)],
                            [(0, 0), (0.01, 1), (0.12, 0.5), (0.26, 0), (0.45, 0)], seed=220 + k,
                            jitter=0.1, shimmer=0.3, breath=1.0, tone=0.15,
                            formants=((700, 400, 1.0), (1600, 500, 0.6), (3000, 700, 0.3)), drive=0.8)
                      for k in range(3)]
    threats = []
    for k in range(3):
        r = _r(2300 + k)
        f = 140 + 60 * r()
        threats.append(voice("boar_threat%d" % k, 0.8, [(0, f), (0.25, f * 1.6), (0.55, f * 1.2)],
                             [(0, 0), (0.03, 0.8), (0.3, 1), (0.6, 0), (0.8, 0)], seed=230 + k,
                             jitter=0.08, shimmer=0.3, breath=0.4, sub=0.35,
                             formants=((450, 160, 1.0), (1250, 240, 0.7), (2600, 360, 0.3)), drive=2.2,
                             rattle=(28, 0.5)))
    roles["threat"] = threats
    roles["attack"] = [grunt("boar_attack%d" % k, 240 + k, 150 + 30 * k, 0.3, rough=0.1, breath=0.5) for k in range(3)]
    hurts = []
    for k in range(3):
        r = _r(2500 + k)
        f = 900 + 400 * r()
        d = 0.5 + 0.25 * r()
        hurts.append(voice("boar_hurt%d" % k, d + 0.15, [(0, f * 0.8), (0.08, f * 1.1), (d * 0.7, f), (d, f * 0.75)],
                           [(0, 0), (0.02, 1), (d * 0.8, 0.9), (d, 0), (d + 0.15, 0)], seed=250 + k,
                           jitter=0.04, shimmer=0.2, breath=0.3, sub=0.3,
                           formants=((1100, 260, 1.0), (2500, 380, 0.7), (3800, 500, 0.35)), drive=2.5))
    roles["hurt"] = hurts
    roles["death"] = [voice("boar_death%d" % k, 1.4, [(0, 1100 - 150 * k), (0.15, 1250 - 150 * k), (1.1, 500)],
                            [(0, 0), (0.03, 1), (0.8, 0.6), (1.1, 0), (1.4, 0)], seed=260 + k,
                            jitter=0.05, shimmer=0.25, breath=0.35, sub=0.35,
                            formants=((1000, 260, 1.0), (2400, 380, 0.65), (3600, 500, 0.3)), drive=2.2)
                      for k in range(2)]
    return roles


def wolf():
    roles = {}
    howls = []
    for k in range(3):
        r = _r(3100 + k)
        f = 330 + 120 * r()
        d = 2.4 + 1.1 * r()
        rise = 0.45 + 0.25 * r()
        f0 = [(0, f * 0.82), (rise, f * 1.18), (d * 0.55, f * 1.12), (d * 0.8, f * 1.05), (d, f * 0.78)]
        howls.append(voice("wolf_call%d" % k, d + 0.4, f0,
                           [(0, 0), (0.25, 0.85), (rise, 1), (d * 0.8, 0.85), (d, 0), (d + 0.4, 0)], seed=310 + k,
                           jitter=0.004, shimmer=0.02, breath=0.03, lowpass=900,
                           formants=(([(0, 450), (rise, 700), (d, 520)], 260, 1.0), (1300, 300, 0.25),
                                     (2600, 400, 0.08))))
    roles["call"] = howls
    roles["alarm"] = [voice("wolf_alarm%d" % k, 0.42, [(0, 320 + 30 * k), (0.15, 260)],
                            [(0, 0), (0.01, 1), (0.08, 0.6), (0.18, 0), (0.42, 0)], seed=320 + k,
                            jitter=0.05, shimmer=0.2, breath=0.6, tone=0.7, sub=0.2,
                            formants=((650, 200, 1.0), (1400, 280, 0.6), (2700, 380, 0.25)), drive=1.5)
                      for k in range(3)]
    growls = []
    for k in range(3):
        r = _r(3300 + k)
        f = 75 + 30 * r()
        d = 1.1 + 0.6 * r()
        growls.append(voice("wolf_threat%d" % k, d + 0.15, [(0, f), (d * 0.5, f * 1.15), (d, f * 0.9)],
                            [(0, 0), (0.12, 0.9), (d * 0.5, 1), (d, 0), (d + 0.15, 0)], seed=330 + k,
                            jitter=0.09, shimmer=0.35, breath=0.45, sub=0.3,
                            formants=((420, 150, 1.0), (1050, 220, 0.6), (2450, 340, 0.25)), drive=1.8,
                            rattle=(24 + 6 * r(), 0.7)))
    roles["threat"] = growls
    # A snarl: a short high growl with the lips back (higher formants).
    roles["attack"] = [voice("wolf_attack%d" % k, 0.5, [(0, 150 + 20 * k), (0.2, 190), (0.35, 140)],
                             [(0, 0), (0.02, 1), (0.25, 0.8), (0.35, 0), (0.5, 0)], seed=340 + k,
                             jitter=0.1, shimmer=0.35, breath=0.6, sub=0.3,
                             formants=((600, 180, 1.0), (1600, 260, 0.7), (2900, 380, 0.3)), drive=2.2,
                             rattle=(32, 0.6)) for k in range(3)]
    roles["snap"] = [click("wolf_snap%d" % k, 2600 + 400 * k, 0.006, seconds=0.06, noise=0.6, seed=350 + k)
                     for k in range(3)]
    yelps = []
    for k in range(3):
        r = _r(3600 + k)
        f = 800 + 250 * r()
        yelps.append(voice("wolf_hurt%d" % k, 0.36, [(0, f * 1.1), (0.04, f * 1.25), (0.2, f * 0.6)],
                           [(0, 0), (0.008, 1), (0.12, 0.6), (0.22, 0), (0.36, 0)], seed=360 + k,
                           jitter=0.02, shimmer=0.08, breath=0.15,
                           formants=((900, 200, 1.0), (2200, 300, 0.55), (3300, 420, 0.25)), drive=0.9))
    roles["hurt"] = yelps
    roles["death"] = [voice("wolf_death%d" % k, 1.2, [(0, 700 - 60 * k), (0.1, 760 - 60 * k), (0.9, 300)],
                            [(0, 0), (0.03, 1), (0.5, 0.6), (0.9, 0), (1.2, 0)], seed=370 + k,
                            jitter=0.03, shimmer=0.12, breath=0.25, sub=0.15,
                            formants=((800, 200, 1.0), (2000, 300, 0.5), (3100, 420, 0.2)), drive=0.6)
                      for k in range(2)]
    return roles


def bear():
    roles = {}
    # A huff: a hard breath out, a low grunt under it.
    roles["call"] = [voice("bear_call%d" % k, 0.75, [(0, 70 + 8 * k), (0.4, 60)],
                           [(0, 0), (0.03, 1), (0.2, 0.55), (0.45, 0), (0.75, 0)], seed=410 + k,
                           jitter=0.1, shimmer=0.35, breath=0.8, tone=0.45, sub=0.3,
                           formants=((320, 160, 1.0), (850, 260, 0.6), (2000, 400, 0.25), (160, 70, 0.5)), drive=1.2)
                     for k in range(3)]
    roles["alarm"] = [voice("bear_alarm%d" % k, 0.55, [(0, 90), (0.3, 75)],
                            [(0, 0), (0.01, 1), (0.1, 0.4), (0.3, 0), (0.55, 0)], seed=420 + k,
                            jitter=0.12, shimmer=0.4, breath=1.0, tone=0.3,
                            formants=((380, 220, 1.0), (950, 320, 0.6), (2100, 450, 0.25)), drive=1.0)
                      for k in range(3)]
    roars = []
    for k in range(3):
        r = _r(4300 + k)
        f = 110 + 40 * r()
        d = 1.5 + 0.6 * r()
        roars.append(voice("bear_threat%d" % k, d + 0.3, [(0, f * 0.8), (d * 0.3, f * 1.3), (d * 0.7, f * 1.1), (d, f * 0.7)],
                           [(0, 0), (0.08, 0.7), (d * 0.3, 1), (d * 0.75, 0.85), (d, 0), (d + 0.3, 0)], seed=430 + k,
                           jitter=0.07, shimmer=0.3, breath=0.5, sub=0.4,
                           formants=((480, 170, 1.0), (1050, 230, 0.75), (2300, 340, 0.35), (220, 90, 0.5)),
                           drive=2.6, rattle=(18, 0.35)))
    roles["threat"] = roars
    roles["attack"] = [voice("bear_attack%d" % k, 0.8, [(0, 130), (0.15, 170 + 15 * k), (0.55, 110)],
                             [(0, 0), (0.03, 1), (0.4, 0.8), (0.6, 0), (0.8, 0)], seed=440 + k,
                             jitter=0.08, shimmer=0.3, breath=0.5, sub=0.4,
                             formants=((500, 170, 1.0), (1100, 230, 0.7), (2400, 340, 0.3)), drive=2.8,
                             rattle=(20, 0.3)) for k in range(3)]
    roles["pop"] = [click("bear_pop%d" % k, 900 + 200 * k, 0.012, seconds=0.1, noise=0.35, seed=450 + k)
                    for k in range(3)]
    roles["hurt"] = [voice("bear_hurt%d" % k, 0.9, [(0, 170 + 20 * k), (0.1, 200 + 20 * k), (0.65, 120)],
                           [(0, 0), (0.03, 1), (0.45, 0.75), (0.65, 0), (0.9, 0)], seed=460 + k,
                           jitter=0.06, shimmer=0.25, breath=0.4, sub=0.35,
                           formants=((560, 180, 1.0), (1200, 250, 0.7), (2500, 360, 0.3)), drive=2.2)
                     for k in range(3)]
    roles["death"] = [voice("bear_death%d" % k, 1.8, [(0, 150 - 10 * k), (0.2, 165), (1.5, 70)],
                            [(0, 0), (0.05, 1), (0.9, 0.7), (1.5, 0), (1.8, 0)], seed=470 + k,
                            jitter=0.07, shimmer=0.3, breath=0.45, sub=0.4,
                            formants=((450, 170, 1.0), (1000, 240, 0.65), (2200, 340, 0.3)), drive=2.0)
                      for k in range(2)]
    return roles


def hooves():
    """A hoof's tock on soft ground, light (deer) and lighter (boar)."""
    return {"deer": [click("hoof_deer%d" % k, 1300 + 180 * k, 0.005, seconds=0.07, noise=0.5, seed=510 + k)
                     for k in range(4)],
            "boar": [click("hoof_boar%d" % k, 1700 + 200 * k, 0.004, seconds=0.06, noise=0.5, seed=520 + k)
                     for k in range(4)]}


def everything():
    return {"deer": deer(), "boar": boar(), "wolf": wolf(), "bear": bear()}


if __name__ == "__main__":
    import time
    t = time.time()
    made = everything()
    print(sum(len(v) for roles in made.values() for v in roles.values()), "voices in %.1f s" % (time.time() - t))
