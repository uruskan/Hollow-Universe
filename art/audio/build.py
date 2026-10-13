"""Builds the ground game's sounds (GROUND.md §4.9, §9.2) from CC0 recordings and writes the catalogue.

    python -I art/audio/build.py                  every sound that changed, then the catalogue and the levels
    python -I art/audio/build.py --only footstep  sounds whose path starts with "footstep" (several: a,b)
    python -I art/audio/build.py --force          rebuild everything, even what didn't change
    python -I art/audio/build.py --catalogue      only rewrite game/data/ground/sounds.json
    python -I art/audio/build.py --credits        print the THIRD_PARTY.md table of the recordings used

After Godot first imports new files (`$GODOT --headless --path game --import`), run the build again: it sets the
import options Godot can't guess (loops loop; interface WAVs stay uncompressed PCM) and Godot re-imports them.

Each sound is a recipe (events.py): parts of recordings, each with its own gain, varispeed pitch, filters and
offset, mixed, filtered, cut where it starts and where its tail has died away (or made into a seamless loop by a
crossfade), brought to its category's loudness (ITU-R BS.1770: the highest momentary loudness of a one-shot, the
integrated loudness of a loop) without passing -1 dBTP, and encoded as Ogg Vorbis at 48 kHz (short interface clicks
as 16-bit WAV). Every file is measured again after encoding; game/assets/audio/levels.json keeps the measurements
with each file's SHA-256, and the tests check them (tests/Hollow.Tests/SoundCatalogueTests.cs).

Encoding is bit-exact (no random Ogg serials, no encoder tags), so rebuilding unchanged recipes writes the same bytes.
"""

import concurrent.futures
import hashlib
import json
import os
import sys
import threading

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import audiolib  # noqa: E402
import sources  # noqa: E402

REPO = sources.REPO
ASSETS = os.path.join(REPO, "game", "assets", "audio")
LEVELS = os.path.join(ASSETS, "levels.json")
CATALOGUE = os.path.join(REPO, "game", "data", "ground", "sounds.json")
WORK = os.path.join(REPO, "art", ".cache", "audio", "work")
RATE = audiolib.RATE
TRUE_PEAK_CEILING = -1.0  # dBTP, the tests' limit
ENCODE_CEILING = -1.6  # dBTP aimed at before encoding; Vorbis adds a few tenths

# Loudness per category: (metric, target, lowest, highest, most limiting allowed in dB), in LUFS. "event" is a
# one-shot's loudness over its own length (the highest momentary loudness, corrected for sounds shorter than the
# 400 ms window, audiolib.event_loudness); "integrated" the gated loudness of a loop.
CATEGORIES = {
    "footstep": ("event", -26.0, -30.0, -22.0, 2.0),
    "foley": ("event", -23.0, -27.0, -19.0, 3.0),
    "click": ("event", -27.0, -33.0, -21.0, 6.0),
    "gather": ("event", -19.0, -23.0, -15.0, 4.0),
    "impact": ("event", -21.0, -27.0, -17.0, 10.0),
    "voice": ("event", -21.0, -25.0, -17.0, 2.0),
    "gunshot": ("event", -20.0, -25.0, -16.0, 6.0),
    "distant": ("event", -22.0, -27.0, -17.0, 6.0),
    "explosion": ("event", -13.0, -18.0, -9.0, 10.0),
    "ui": ("event", -27.0, -33.0, -21.0, 1.0),
    "marker": ("event", -23.0, -28.0, -18.0, 1.0),
    "loop": ("integrated", -29.0, -34.0, -24.0, 4.0),
    "ambience": ("integrated", -30.0, -34.0, -26.0, 1.0),
    "accent": ("event", -27.0, -32.0, -22.0, 1.0),
}


# Categories whose limiter lets go quickly, so a shot's body isn't ducked behind its crack.
FAST = {"gunshot", "explosion", "impact", "click"}


class Seg:
    """A part of a recording: from `start` for `dur` seconds (None: to the end), at `at` seconds in the sound."""

    def __init__(self, src, start=0.0, dur=None, gain=0.0, pitch=1.0, fx="", at=0.0, fade_in=0.0, fade_out=0.0,
                 reverse=False, ir=None, wet=1.0, dry=0.0, post=""):
        self.src, self.start, self.dur, self.gain, self.pitch = src, start, dur, gain, pitch
        self.fx, self.at, self.fade_in, self.fade_out, self.reverse = fx, at, fade_in, fade_out, reverse
        # ir: convolve with this impulse response (wet and dry gains), then `post` filters.
        self.ir, self.wet, self.dry, self.post = ir, wet, dry, post

    def key(self):
        rel = os.path.relpath(self.src, sources.DOWNLOADS if self.src.startswith(sources.DOWNLOADS) else sources.CACHE)
        return [rel.replace("\\", "/"), os.path.getsize(self.src), round(self.start, 4),
                None if self.dur is None else round(self.dur, 4), self.gain, self.pitch, self.fx, self.at,
                self.fade_in, self.fade_out, self.reverse,
                os.path.basename(self.ir) if self.ir else None, self.wet, self.dry, self.post]


class Out:
    """One sound file: `rel` under game/assets/audio."""

    def __init__(self, rel, category, segs, fx="", channels=1, fade_in=0.002, tail_db=-48.0, max_fade=0.25,
                 length=None, loop=None, quality=5, lead_db=-45.0):
        self.rel, self.category, self.segs, self.fx, self.channels = rel, category, segs, fx, channels
        self.fade_in, self.tail_db, self.max_fade, self.length = fade_in, tail_db, max_fade, length
        self.loop, self.quality, self.lead_db = loop, quality, lead_db
        self.wav = rel.endswith(".wav")

    def recipe(self):
        data = [self.rel, self.category, CATEGORIES[self.category], [s.key() for s in self.segs], self.fx,
                self.channels, self.fade_in, self.tail_db, self.max_fade, self.length, self.loop, self.quality,
                self.lead_db, 3]
        return hashlib.sha256(json.dumps(data).encode()).hexdigest()[:16]


def layout(channels):
    return "mono" if channels == 1 else "stereo"


def render_mix(out, path):
    """Mixes the parts into a float WAV."""
    args = []
    parts = []
    inputs = 0
    for i, s in enumerate(out.segs):
        if s.start:
            args += ["-ss", "%.5f" % s.start]
        if s.dur:
            args += ["-t", "%.5f" % s.dur]
        args += ["-i", s.src]
        src_index = inputs
        inputs += 1
        ir_index = None
        if s.ir:
            args += ["-i", s.ir]
            ir_index = inputs
            inputs += 1
        chain = ["aresample=%d" % RATE, "aformat=sample_fmts=flt:channel_layouts=%s" % layout(out.channels)]
        if s.reverse:
            chain.append("areverse")
        if s.fade_in:
            chain.append("afade=t=in:d=%.4f" % s.fade_in)
        if s.fade_out and s.dur:
            chain.append("afade=t=out:st=%.4f:d=%.4f" % (max(0.0, s.dur - s.fade_out), s.fade_out))
        if s.pitch != 1.0:
            chain += ["asetrate=%d" % round(RATE * s.pitch), "aresample=%d" % RATE]
        if s.fx:
            chain.append(s.fx)
        if ir_index is not None:
            # Pad so the convolution's tail fits, then convolve with the impulse response.
            parts.append("[%d:a]%s,apad=pad_dur=3[p%d]" % (src_index, ",".join(chain), i))
            parts.append("[%d:a]aresample=%d,aformat=sample_fmts=flt:channel_layouts=mono[ir%d]" % (ir_index, RATE, i))
            chain = ["afir=dry=%.3f:wet=%.3f:gtype=none:irnorm=-1:irfmt=mono:maxir=10" % (s.dry, s.wet)]
            if s.post:
                chain.append(s.post)
            head = "[p%d][ir%d]" % (i, i)
        else:
            head = "[%d:a]" % src_index
        if s.gain:
            chain.append("volume=%.2fdB" % s.gain)
        if s.at > 0:
            chain.append("adelay=%d:all=1" % round(s.at * 1000))
        parts.append("%s%s[s%d]" % (head, ",".join(chain), i))
    n = len(out.segs)
    mix = "".join("[s%d]" % i for i in range(n))
    mix += "amix=inputs=%d:normalize=0:duration=longest:dropout_transition=0" % n if n > 1 else "anull"
    post = [mix]
    if out.fx:
        post.append(out.fx)
    post.append("aformat=sample_fmts=flt:channel_layouts=%s" % layout(out.channels))
    graph = ";".join(parts) + ";" + ",".join(post) + "[out]"
    audiolib.run(args + ["-filter_complex", graph, "-map", "[out]", "-ar", str(RATE), "-c:a", "pcm_f32le", path])


def cut(out, src, dst):
    """Cuts the lead before the sound starts and the tail after it has died away, with short fades."""
    x = audiolib.decode(src, 16000, mono=True)
    env = audiolib.envelope(x, 16000, 5.0, 10.0)
    if not env:
        raise RuntimeError("%s: empty" % out.rel)
    top = max(env)
    first = next(k for k, v in enumerate(env) if v > top + out.lead_db)
    last = max(k for k, v in enumerate(env) if v > top + out.tail_db)
    t0 = max(0.0, first * 0.005 - 0.004)
    t1 = (last + 1) * 0.005 + 0.010
    if out.length:
        t1 = min(t1, t0 + out.length)
    total = len(x) / 16000.0
    t1 = min(t1, total)
    fade = min(out.max_fade, max(0.02, (t1 - t0) * 0.3))
    chain = "atrim=start=%.5f:end=%.5f,asetpts=PTS-STARTPTS" % (t0, t1)
    if out.fade_in:
        chain += ",afade=t=in:d=%.4f" % out.fade_in
    chain += ",afade=t=out:st=%.5f:d=%.5f:curve=exp" % (max(0.0, t1 - t0 - fade), fade)
    audiolib.run(["-i", src, "-af", chain, "-c:a", "pcm_f32le", dst])


def make_loop(out, src, dst):
    """A seamless loop of `length` seconds: the start crossfades with what follows the end (equal power)."""
    length, overlap = out.loop
    graph = ("[0:a]asplit=3[a][b][c];"
             "[a]atrim=0:%.5f,asetpts=PTS-STARTPTS,afade=t=in:d=%.5f:curve=qsin[h];"
             "[b]atrim=%.5f:%.5f,asetpts=PTS-STARTPTS,afade=t=out:d=%.5f:curve=qsin[t];"
             "[h][t]amix=inputs=2:normalize=0[m];"
             "[c]atrim=%.5f:%.5f,asetpts=PTS-STARTPTS[body];"
             "[m][body]concat=n=2:v=0:a=1[out]") % (overlap, overlap, length, length + overlap, overlap, overlap,
                                                    length)
    audiolib.run(["-i", src, "-filter_complex", graph, "-map", "[out]", "-c:a", "pcm_f32le", dst])


def level_of(m, metric):
    return m[metric]


def encode(out, src, dst, gain, limit):
    chain = ["volume=%.3fdB" % gain]
    if limit:
        # A look-ahead limiter at limit dBFS (sample peak); true peaks are checked after encoding.
        chain.append("alimiter=limit=%.4f:attack=1:release=%d:level=0" % (10 ** (limit / 20.0),
                                                                          30 if out.category in FAST else 60))
    chain.append("aformat=channel_layouts=%s" % layout(out.channels))
    args = ["-i", src, "-af", ",".join(chain), "-ar", str(RATE), "-fflags", "+bitexact", "-flags", "+bitexact",
            "-map_metadata", "-1"]
    if out.wav:
        args += ["-c:a", "pcm_s16le"]
    else:
        args += ["-c:a", "libvorbis", "-q:a", str(out.quality)]
    audiolib.run(args + [dst])


def build(out):
    metric, target, low, high, max_limit = CATEGORIES[out.category]
    base = os.path.join(WORK, out.rel.replace("/", "__"))
    os.makedirs(WORK, exist_ok=True)
    mix, shaped = base + ".mix.wav", base + ".shaped.wav"
    render_mix(out, mix)
    if out.loop:
        make_loop(out, mix, shaped)
    else:
        cut(out, mix, shaped)
    m = audiolib.measure(shaped)
    gain = target - level_of(m, metric)
    over = m["truePeak"] + gain - ENCODE_CEILING
    limit = None
    if over > 0:
        if over > max_limit:
            gain -= over - max_limit
        if max_limit > 0:
            limit = ENCODE_CEILING - 0.2
    dst = os.path.join(ASSETS, out.rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    tmp = dst + ".part" + os.path.splitext(dst)[1]
    for _ in range(8):
        encode(out, shaped, tmp, gain, limit)
        final = audiolib.measure(tmp)
        if final["truePeak"] <= TRUE_PEAK_CEILING - 0.05:
            break
        excess = final["truePeak"] - TRUE_PEAK_CEILING + 0.15
        # Encoding rings over a limited peak: lower the limiter's ceiling rather than the level.
        if limit is not None:
            limit -= excess
        else:
            gain -= excess
    os.replace(tmp, dst)
    for p in (mix, shaped):
        try:
            os.remove(p)
        except OSError:
            pass
    final["category"] = out.category
    final["gain"] = round(gain, 2)
    final["recipe"] = out.recipe()
    final["sha256"] = sha256(dst)
    final["bytes"] = os.path.getsize(dst)
    return final


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        h.update(f.read())
    return h.hexdigest()


def load_json(path, default):
    if os.path.isfile(path):
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    return default


def write_json(path, data, compact_keys=()):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    text = json.dumps(data, indent=2, ensure_ascii=False)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text + "\n")


def write_levels(levels):
    """One line per file, sorted, so diffs stay readable."""
    os.makedirs(os.path.dirname(LEVELS), exist_ok=True)
    keys = sorted(levels)
    with open(LEVELS, "w", encoding="utf-8", newline="\n") as f:
        f.write("{\n")
        f.write('  "source": "Measured by art/audio/build.py after encoding (ffmpeg ebur128: momentary and integrated '
                'loudness in LUFS, true peak in dBTP; high and low: energy above 10 kHz and below 40 Hz relative to '
                'the whole, dB; lead: ms before the sound; tail: the last 15 ms relative to the loudest, dB).",\n')
        f.write('  "files": {\n')
        for i, k in enumerate(keys):
            f.write('    %s: %s%s\n' % (json.dumps(k), json.dumps(levels[k], sort_keys=True),
                                       "," if i < len(keys) - 1 else ""))
        f.write("  }\n}\n")


def main(argv):
    import events

    if "--credits" in argv:
        events.everything()
        print(credits())
        return 0
    outs, catalogue = events.everything()
    if "--catalogue" not in argv:
        only = None
        if "--only" in argv:
            only = argv[argv.index("--only") + 1].split(",")
        force = "--force" in argv
        levels = load_json(LEVELS, {"files": {}}).get("files", {})
        todo = []
        seen = set()
        for o in outs:
            if o.rel in seen:
                raise SystemExit("two recipes write %s" % o.rel)
            seen.add(o.rel)
            if only and not any(o.rel.startswith(p) for p in only):
                continue
            dst = os.path.join(ASSETS, o.rel)
            old = levels.get(o.rel)
            if not force and old and old.get("recipe") == o.recipe() and os.path.isfile(dst) \
                    and old.get("sha256") == sha256(dst):
                continue
            todo.append(o)
        print("%d sounds, %d to build" % (len(outs), len(todo)))
        lock = threading.Lock()
        failures = []

        def job(o):
            try:
                m = build(o)
            except Exception as e:  # noqa: BLE001
                with lock:
                    failures.append((o.rel, str(e)))
                    print("FAILED", o.rel, str(e)[-600:])
                return
            with lock:
                levels[o.rel] = m
                metric, target, low, high, _ = CATEGORIES[o.category]
                value = level_of(m, metric)
                flag = "" if low <= value <= high and m["truePeak"] <= TRUE_PEAK_CEILING else "  <-- OUT OF RANGE"
                print("%-52s %6.1f LUFS %6.2f dBTP %5.2f s%s" % (o.rel, value, m["truePeak"], m["duration"], flag))

        with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
            list(pool.map(job, todo))
        # Files no recipe writes any more go, with their measurements.
        for rel in list(levels):
            if rel not in seen:
                del levels[rel]
                p = os.path.join(ASSETS, rel)
                for path in (p, p + ".import"):
                    if os.path.isfile(path):
                        os.remove(path)
        write_levels(levels)
        if failures:
            print("%d failed" % len(failures))
            return 1
    write_catalogue(catalogue)
    changed = fix_imports(outs)
    if changed:
        print("%d .import files updated: run Godot's import again" % changed)
    total = sum(os.path.getsize(os.path.join(ASSETS, o.rel)) for o in outs if os.path.isfile(os.path.join(ASSETS, o.rel)))
    print("catalogue: %d events; audio: %.1f MB" % (len(catalogue), total / 1e6))
    return 0


def fix_imports(outs):
    """Import options per file: loops loop (Ogg Vorbis `loop`), interface WAVs stay PCM (`compress/mode=0`)."""
    changed = 0
    for o in outs:
        path = os.path.join(ASSETS, o.rel) + ".import"
        if not os.path.isfile(path):
            continue
        with open(path, encoding="utf-8") as f:
            text = f.read()
        new = text
        if o.wav:
            new = new.replace("compress/mode=2", "compress/mode=0").replace("compress/mode=1", "compress/mode=0")
        else:
            want = "loop=true" if o.loop else "loop=false"
            new = new.replace("loop=true", want).replace("loop=false", want)
        if new != text:
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(new)
            changed += 1
    return changed


def write_catalogue(events_list):
    """sounds.json: the build owns each event's files and category; every other field is kept as edited."""
    old = load_json(CATALOGUE, {})
    by_key = {e["key"]: e for e in old.get("events", [])}
    merged = []
    for e in events_list:
        cur = by_key.pop(e["key"], None)
        if cur is None:
            merged.append(e)
            continue
        for field in ("category", "files", "loop"):
            if field in e:
                cur[field] = e[field]
            elif field in cur and field != "loop":
                del cur[field]
        if "layers" in e:
            old_layers = {l["name"]: l for l in cur.get("layers", [])}
            layers = []
            for l in e["layers"]:
                ol = old_layers.get(l["name"])
                if ol:
                    ol["files"] = l["files"]
                    layers.append(ol)
                else:
                    layers.append(l)
            cur["layers"] = layers
        merged.append(cur)
    # Events added by hand (not built here) stay.
    merged += by_key.values()
    data = {
        "source": old.get("source", "GROUND.md §4.9. Written by art/audio/build.py, which owns each event's files "
                                    "and category; every other field may be tuned by hand and is kept."),
        "categories": {k: {"metric": v[0], "target": v[1], "min": v[2], "max": v[3]} for k, v in CATEGORIES.items()},
        "events": merged,
    }
    write_catalogue_json(data)


def write_catalogue_json(data):
    """Readable JSON: one line per event field, files on their own lines."""
    lines = ["{", '  "source": %s,' % json.dumps(data["source"], ensure_ascii=False), '  "categories": {']
    cats = list(data["categories"].items())
    for i, (k, v) in enumerate(cats):
        lines.append('    %s: %s%s' % (json.dumps(k), json.dumps(v), "," if i < len(cats) - 1 else ""))
    lines.append("  },")
    lines.append('  "events": [')
    evs = data["events"]
    for i, e in enumerate(evs):
        lines.append("    {")
        fields = list(e.items())
        for j, (k, v) in enumerate(fields):
            comma = "," if j < len(fields) - 1 else ""
            if k == "files":
                lines.append('      "files": [')
                for n, f in enumerate(v):
                    lines.append('        %s%s' % (json.dumps(f), "," if n < len(v) - 1 else ""))
                lines.append("      ]" + comma)
            elif k == "layers":
                lines.append('      "layers": [')
                for n, l in enumerate(v):
                    head = {a: b for a, b in l.items() if a != "files"}
                    lines.append('        { %s, "files": [' % json.dumps(head)[1:-1])
                    for q, f in enumerate(l["files"]):
                        lines.append('          %s%s' % (json.dumps(f), "," if q < len(l["files"]) - 1 else ""))
                    lines.append("        ] }" + ("," if n < len(v) - 1 else ""))
                lines.append("      ]" + comma)
            else:
                lines.append('      %s: %s%s' % (json.dumps(k), json.dumps(v, ensure_ascii=False), comma))
        lines.append("    }" + ("," if i < len(evs) - 1 else ""))
    lines.append("  ]")
    lines.append("}")
    os.makedirs(os.path.dirname(CATALOGUE), exist_ok=True)
    with open(CATALOGUE, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


def credits():
    """The THIRD_PARTY.md rows of every recording the recipes use."""
    rows = ["| Download | Licence | SHA-256 | Used for |", "|---|---|---|---|"]
    used_packs = sorted({n for (n, _) in sources.USED if n != "bigsoundbank"})
    for n in used_packs:
        p = sources.PACKS[n]
        members = sorted(m for (k, m) in sources.USED if k == n)
        rows.append("| %s, %s: `%s`, %s (page: %s), %s bytes | %s | `%s` | %d files: %s |" % (
            p.author, p.title, p.file, p.url, p.page, format(p.size, ","), p.licence, p.sha256, len(members),
            ", ".join("`%s`" % m.split("/")[-1] for m in members[:12]) + (" ..." if len(members) > 12 else "")))
    rows.append("")
    rows.append("| BigSoundBank id | Title | File | SHA-256 |")
    rows.append("|---|---|---|---|")
    for sid in sorted(m for (k, m) in sources.USED if k == "bigsoundbank"):
        v = sources.BIGSOUNDBANK[sid]
        rows.append("| [%s](https://bigsoundbank.com/s%s.html) | %s | https://bigsoundbank.com/UPLOAD/bwf-en/%s.wav "
                    "(%s bytes) | `%s` |" % (sid, sid, v["title"], sid, format(v["bytes"], ","), v["sha256"]))
    return "\n".join(rows)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
