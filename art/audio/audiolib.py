"""Shared helpers of the sound pipeline (GROUND.md §4.9): ffmpeg, decoding, onset slicing and measurement.

Everything heavy runs in ffmpeg; Python only analyses envelopes. Measurements follow ITU-R BS.1770 (ffmpeg's
ebur128 filter): integrated loudness for loops, the highest momentary loudness (400 ms window) for one-shots, and the
true peak (4x oversampled).
"""

import array
import math
import os
import re
import shutil
import subprocess

RATE = 48000

_FFMPEG_CANDIDATES = [
    os.environ.get("HOLLOW_FFMPEG", ""),
    "D:/GameDev/Tools/ScreenCapture/ShareX/ffmpeg.exe",
    shutil.which("ffmpeg") or "",
]


def ffmpeg():
    for path in _FFMPEG_CANDIDATES:
        if path and os.path.isfile(path):
            return path
    raise SystemExit("ffmpeg not found: set HOLLOW_FFMPEG")


def run(args, quiet=True):
    """Runs ffmpeg with args; returns stderr (its log). Raises with the log on failure."""
    cmd = [ffmpeg(), "-hide_banner", "-nostdin", "-y"] + (["-nostats"] if quiet else []) + list(args)
    p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    log = p.stderr.decode("utf-8", "replace")
    if p.returncode != 0:
        raise RuntimeError("ffmpeg failed:\n" + " ".join(cmd) + "\n" + log[-3000:])
    return log


def decode(path, rate=16000, start=0.0, duration=None, mono=True):
    """Samples of a file (or a part of it) as floats, mono by default, at `rate`."""
    args = [ffmpeg(), "-hide_banner", "-nostdin", "-v", "error"]
    if start:
        args += ["-ss", "%.4f" % start]
    if duration:
        args += ["-t", "%.4f" % duration]
    args += ["-i", path, "-ac", "1" if mono else "2", "-ar", str(rate), "-f", "f32le", "-"]
    p = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if p.returncode != 0:
        raise RuntimeError(p.stderr.decode("utf-8", "replace"))
    a = array.array("f")
    a.frombytes(p.stdout[: len(p.stdout) // 4 * 4])
    return a


def envelope(samples, rate, hop_ms=5.0, window_ms=10.0):
    """RMS level in dBFS every hop_ms, over window_ms."""
    hop = max(1, int(rate * hop_ms / 1000))
    win = max(hop, int(rate * window_ms / 1000))
    sq = [x * x for x in samples]
    out = []
    n = len(sq)
    acc = sum(sq[:win])
    i = 0
    while i + win <= n:
        out.append(10 * math.log10(acc / win + 1e-12))
        nxt = i + hop
        acc += sum(sq[i + win: nxt + win]) - sum(sq[i:nxt])
        i = nxt
    return out


def percentile(values, q):
    s = sorted(values)
    if not s:
        return -120.0
    k = min(len(s) - 1, max(0, int(round(q * (len(s) - 1)))))
    return s[k]


class Event:
    """A sound event found in a recording: start and end in seconds, peak level (dB RMS over 10 ms)."""

    def __init__(self, start, end, peak):
        self.start, self.end, self.peak = start, end, peak

    @property
    def length(self):
        return self.end - self.start

    def __repr__(self):
        return "Event(%.3f-%.3f, %.1f dB)" % (self.start, self.end, self.peak)


def find_events(path, start=0.0, duration=None, rise_db=9.0, above_floor_db=14.0, below_peak_db=30.0,
                end_db=28.0, hold_ms=40.0, min_gap=0.12, max_len=1.5, pre_ms=6.0, rate=16000):
    """Onsets in a recording and where each one dies away.

    An onset is a rise of `rise_db` within 25 ms to a level `above_floor_db` over the recording's noise floor (its
    20th percentile) and within `below_peak_db` of its loudest moment. An event ends when its level has stayed
    `end_db` under its own peak (or near the floor) for `hold_ms`, at the next onset, or after `max_len` seconds.
    """
    x = decode(path, rate, start, duration)
    hop = 5.0
    env = envelope(x, rate, hop)
    if not env:
        return []
    floor = percentile(env, 0.2)
    top = max(env)
    on = max(floor + above_floor_db, top - below_peak_db)
    back = int(25 / hop)
    hold = max(1, int(hold_ms / hop))
    events = []
    i = back
    n = len(env)
    while i < n:
        if env[i] >= on and env[i] - min(env[i - back:i]) >= rise_db:
            if events and (i * hop / 1000.0) - events[-1].start < min_gap:
                i += 1
                continue
            # The attack starts where the level left the floor, at most 30 ms back.
            s = i
            while s > 0 and i - s < int(30 / hop) and env[s - 1] > floor + 3:
                s -= 1
            peak = env[i]
            j = i
            quiet = 0
            while j < n and (j - s) * hop / 1000.0 < max_len:
                peak = max(peak, env[j])
                if env[j] < peak - end_db or env[j] < floor + 4:
                    quiet += 1
                    if quiet >= hold:
                        break
                else:
                    quiet = 0
                # A new onset ends this event.
                if j > i + int(60 / hop) and env[j] - min(env[j - back:j]) >= rise_db + 3 and env[j] >= on:
                    break
                j += 1
            t0 = max(0.0, s * hop / 1000.0 - pre_ms / 1000.0)
            events.append(Event(start + t0, start + j * hop / 1000.0, peak))
            i = j
        i += 1
    return events


def find_steps(path, start=0.0, duration=None, min_sep=0.28, smooth_ms=40.0, above_floor_db=10.0, below_top_db=24.0,
               end_db=24.0, max_len=0.6, max_pre=0.12, rate=16000):
    """Separate impacts in a sequence (footsteps, chops, hits): peaks of a smoothed envelope at least `min_sep`
    apart, each from where it leaves the floor to where it has fallen `end_db` under its peak (or the next one)."""
    x = decode(path, rate, start, duration)
    hop = 5.0
    fine = envelope(x, rate, hop, 10.0)
    smooth = envelope(x, rate, hop, smooth_ms)
    if not smooth:
        return []
    floor = percentile(fine, 0.15)
    top = max(smooth)
    gate = max(percentile(smooth, 0.15) + above_floor_db, top - below_top_db)
    sep = max(1, int(min_sep * 1000 / hop / 2))
    peaks = []
    for i in range(len(smooth)):
        v = smooth[i]
        if v < gate:
            continue
        lo, hi = max(0, i - sep), min(len(smooth), i + sep + 1)
        if v >= max(smooth[lo:hi]) and (not peaks or i - peaks[-1] > sep):
            peaks.append(i)
    events = []
    shift = int((smooth_ms - 10.0) / 2 / hop)  # the smoothed window lags the fine one
    for k, p in enumerate(peaks):
        p = max(0, p - shift)
        peak = max(fine[max(0, p - 8):p + 8] or [fine[p]])
        prev_end = int(events[-1].end * 1000 / hop) if events else 0
        s = p
        while s > prev_end and (p - s) * hop / 1000 < max_pre and fine[s - 1] > floor + 6 and fine[s - 1] > peak - 30:
            s -= 1
        nxt = max(0, peaks[k + 1] - shift - int(max_pre * 1000 / hop / 2)) if k + 1 < len(peaks) else len(fine)
        e = p
        quiet = 0
        while e < min(len(fine), nxt) and (e - s) * hop / 1000 < max_len:
            if fine[e] < peak - end_db or fine[e] < floor + 3:
                quiet += 1
                if quiet >= 6:
                    break
            else:
                quiet = 0
            e += 1
        events.append(Event(s * hop / 1000, e * hop / 1000, peak))
    for ev in events:
        ev.start += start
        ev.end += start
    return events


_FRAME = re.compile(r"t:\s*([0-9.]+)\s+TARGET:.*?M:\s*(-?[0-9.inf]+)\s+S:\s*(-?[0-9.inf]+)")


def loudness_track(path):
    """(t, momentary, short-term) every 100 ms."""
    log = run(["-i", path, "-af", "ebur128=framelog=info", "-f", "null", "-"])
    out = []
    for m in _FRAME.finditer(log):
        out.append((float(m.group(1)), _num(m.group(2)), _num(m.group(3))))
    return out


def _num(s):
    try:
        return float(s)
    except ValueError:
        return -120.0


def measure(path):
    """Loudness, peaks and spectral checks of a finished file."""
    graph = ("[0:a]apad=pad_dur=0.5,asplit=4[a][b][c][d];"
             "[a]ebur128=peak=true:framelog=info[a1];"
             "[b]astats=metadata=0[b1];"
             "[c]highpass=f=10000,highpass=f=10000,astats=metadata=0[c1];"
             "[d]lowpass=f=40,lowpass=f=40,astats=metadata=0[d1];"
             "[a1][b1][c1][d1]amix=inputs=4[out]")
    log = run(["-i", path, "-filter_complex", graph, "-map", "[out]", "-f", "null", "-"])
    probe = run(["-i", path, "-f", "null", "-t", "0", "-"])
    m = re.search(r"Audio: (\w+).*?, (\d+) Hz, (mono|stereo|[0-9.]+ channels)", probe)
    dur = re.search(r"Duration: (\d+):(\d+):([0-9.]+)", probe)
    momentary = [_num(x.group(2)) for x in _FRAME.finditer(log)]
    integ = re.search(r"Integrated loudness:\s*\n\s*I:\s*(-?[0-9.]+) LUFS", log)
    tp = re.search(r"True peak:\s*\n\s*Peak:\s*(-?[0-9.inf]+) dBFS", log)
    stats = _astats(log)
    names = sorted(stats)  # astats instances in graph order: full, high band, low band
    full = stats[names[0]] if names else {}
    high = stats[names[1]] if len(names) > 1 else {}
    low = stats[names[2]] if len(names) > 2 else {}
    seconds = int(dur.group(1)) * 3600 + int(dur.group(2)) * 60 + float(dur.group(3)) if dur else 0.0
    x = decode(path, 16000)
    env = envelope(x, 16000, 5.0)
    top = max(env) if env else -120.0
    lead = next((k for k, v in enumerate(env) if v > top - 40), 0) * 5.0
    tail = env[-3:]  # the last 15 ms
    tail_db = (max(tail) - top) if tail else 0.0
    rms = full.get("RMS level dB", -120.0)
    peak_m = round(max(momentary), 1) if momentary else -120.0
    return {
        "codec": m.group(1) if m else "?",
        "rate": int(m.group(2)) if m else 0,
        "channels": 1 if m and m.group(3) == "mono" else 2,
        "duration": round(seconds, 3),
        "integrated": round(_num(integ.group(1)), 1) if integ else -70.0,
        "momentary": peak_m,
        "event": event_loudness(peak_m, seconds),
        "truePeak": round(_num(tp.group(1)), 2) if tp else 0.0,
        "samplePeak": round(full.get("Peak level dB", 0.0), 2),
        "dc": round(abs(full.get("DC offset", 0.0)), 5),
        "high": round(high.get("RMS level dB", -120.0) - rms, 1),
        "low": round(low.get("RMS level dB", -120.0) - rms, 1),
        "lead": round(lead, 1),
        "tail": round(tail_db, 1),
    }


def event_loudness(momentary, seconds):
    """The loudness of a one-shot over its own length: the highest momentary loudness (a 400 ms window) corrected
    for a sound shorter than the window, down to 100 ms (the ear integrates about that long)."""
    if momentary <= -100:
        return momentary
    return round(momentary + 10 * math.log10(0.4 / min(0.4, max(0.1, seconds))), 1)


def _astats(log):
    """Overall astats values per filter instance."""
    out = {}
    current = {}
    for line in log.splitlines():
        m = re.match(r"\[Parsed_astats_(\d+) @ [^\]]+\] (.*)", line)
        if not m:
            continue
        key = int(m.group(1))
        text = m.group(2)
        if text.strip() == "Overall":
            current[key] = True
            out[key] = {}
            continue
        if current.get(key) and ":" in text:
            name, value = text.split(":", 1)
            out[key][name.strip()] = _num(value.strip().split()[0]) if value.strip() else 0.0
    return out
