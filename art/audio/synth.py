"""Sounds made in code where that sounds natural (GROUND.md §9.2): an outdoor impulse response for distant gunshot
and explosion tails, and a wind bed. Deterministic: the same seed gives the same samples.

The impulse response is a few early reflections (ground, nearby slopes) and a diffuse tail of noise that decays
exponentially and grows darker with time, as distant echoes do. The wind is filtered noise in two bands whose levels
drift with slow, incommensurate sinusoids (gusts), periodic over the loop's length so it repeats seamlessly.
"""

import array
import hashlib
import math
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import audiolib  # noqa: E402
from longpath import fs  # noqa: E402

RATE = audiolib.RATE
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(os.path.dirname(HERE)), "art", ".cache", "audio", "synth")


class Lcg:
    """A small deterministic noise source (no Python random, so every platform makes the same numbers)."""

    def __init__(self, seed):
        self.s = (seed * 2654435761 + 1) & 0xFFFFFFFF

    def next(self):
        self.s = (1664525 * self.s + 1013904223) & 0xFFFFFFFF
        return self.s / 2147483648.0 - 1.0


def write_wav(path, channels, rate=RATE):
    """Writes float samples (list of channel arrays) as 32-bit float WAV."""
    n = len(channels[0])
    data = array.array("f")
    for i in range(n):
        for c in channels:
            data.append(c[i])
    raw = data.tobytes()
    os.makedirs(fs(os.path.dirname(path)), exist_ok=True)
    with open(fs(path), "wb") as f:
        nch = len(channels)
        f.write(b"RIFF" + struct.pack("<I", 36 + len(raw)) + b"WAVE")
        f.write(b"fmt " + struct.pack("<IHHIIHH", 16, 3, nch, rate, rate * 4 * nch, 4 * nch, 32))
        f.write(b"data" + struct.pack("<I", len(raw)) + raw)


def cached(name, args):
    """The cache path of a made sound: its name and a hash of what it was made from."""
    tag = hashlib.sha256(repr(args).encode()).hexdigest()[:10]
    return os.path.join(OUT, "%s_%s.wav" % (name, tag))


def outdoor_ir(name, seconds=2.6, t60=1.8, reflections=((0.09, 0.45), (0.21, 0.3), (0.38, 0.22), (0.63, 0.16),
                                                         (0.95, 0.1)), bright=7000.0, dark=700.0, seed=1):
    """An outdoor tail: sparse early reflections and a diffuse decay that darkens from `bright` to `dark` Hz."""
    path = cached(name, (seconds, t60, reflections, bright, dark, seed, 1))
    if os.path.isfile(fs(path)):
        return path
    n = int(seconds * RATE)
    rng = Lcg(seed)
    out = [0.0] * n
    k = math.log(1000.0) / t60  # -60 dB at t60
    state = 0.0
    for i in range(n):
        t = i / RATE
        cutoff = bright * (dark / bright) ** min(1.0, t / seconds)
        a = math.exp(-2 * math.pi * cutoff / RATE)
        # The diffuse tail builds up over the first 60 ms.
        env = math.exp(-k * t) * min(1.0, t / 0.06) * 0.35
        state = a * state + (1 - a) * rng.next()
        out[i] = state * env * 2.2
    for at, gain in reflections:
        start = int(at * RATE)
        burst = int(0.004 * RATE)
        st = 0.0
        a = math.exp(-2 * math.pi * 2500.0 / RATE)
        for j in range(burst * 3):
            if start + j >= n:
                break
            st = a * st + (1 - a) * (rng.next() if j < burst else 0.0)
            out[start + j] += st * gain * 6.0
    write_wav(path, [out])
    return path


def wind(name, seconds=40.0, seed=7, body=(0.55, 0.22, 0.12, 0.08), whistle=(0.22, 0.14, 0.08),
         body_band=(70.0, 520.0), whistle_band=(650.0, 1400.0), stereo=True):
    """A wind bed: a low rushing body and a breathy whistle band, each swelling in slow gusts."""
    path = cached(name, (seconds, seed, body, whistle, body_band, whistle_band, stereo, 1))
    if os.path.isfile(fs(path)):
        return path
    n = int(seconds * RATE)
    chans = []
    for ch in range(2 if stereo else 1):
        rng = Lcg(seed * 31 + ch)
        lo1 = hi1 = lo2 = hi2 = 0.0
        a_lo = math.exp(-2 * math.pi * body_band[1] / RATE)
        a_hi = math.exp(-2 * math.pi * body_band[0] / RATE)
        w_lo = math.exp(-2 * math.pi * whistle_band[1] / RATE)
        w_hi = math.exp(-2 * math.pi * whistle_band[0] / RATE)
        b0, b1, b2, b3 = body
        v0, v1, v2 = whistle
        phase = 0.7 * ch
        out = [0.0] * n
        for i in range(n):
            t = i / RATE
            x = rng.next()
            # body: band between body_band (one-pole low-pass minus one-pole low-pass)
            lo1 = a_lo * lo1 + (1 - a_lo) * x
            hi1 = a_hi * hi1 + (1 - a_hi) * x
            band = lo1 - hi1
            lo2 = w_lo * lo2 + (1 - w_lo) * x
            hi2 = w_hi * hi2 + (1 - w_hi) * x
            whis = lo2 - hi2
            # Gusts: whole numbers of cycles in the loop, so the level repeats seamlessly.
            g = b0 + b1 * math.sin(2 * math.pi * 2 * t / seconds + phase) + \
                b2 * math.sin(2 * math.pi * 5 * t / seconds + 1.3 + phase) + \
                b3 * math.sin(2 * math.pi * 11 * t / seconds + 0.4)
            h = v0 + v1 * math.sin(2 * math.pi * 3 * t / seconds + 2.0 + phase) + \
                v2 * math.sin(2 * math.pi * 7 * t / seconds + 0.9)
            out[i] = band * max(0.05, g) * 3.0 + whis * max(0.0, h) * 4.0
        chans.append(out)
    write_wav(path, chans)
    return path


def tones(name, notes, seconds, square=0.0):
    """Electronic beeps (a keypad's piezo): notes = [(frequency Hz, start s, length s)], each with a 4 ms rise and a
    short decay; `square` mixes in odd harmonics for a harsher buzz."""
    path = cached(name, (tuple(notes), seconds, square, 1))
    if os.path.isfile(fs(path)):
        return path
    n = int(seconds * RATE)
    out = [0.0] * n
    for freq, start, length in notes:
        i0 = int(start * RATE)
        m = int(length * RATE)
        for j in range(m):
            if i0 + j >= n:
                break
            t = j / RATE
            env = min(1.0, t / 0.004) * min(1.0, (length - t) / 0.012)
            ph = 2 * math.pi * freq * t
            x = math.sin(ph) + square * (math.sin(3 * ph) / 3 + math.sin(5 * ph) / 5 + math.sin(7 * ph) / 7)
            out[i0 + j] += 0.5 * env * x / (1 + square)
    write_wav(path, [out])
    return path


if __name__ == "__main__":
    print(outdoor_ir("ir_outdoor"))
    print(wind("wind_calm"))
