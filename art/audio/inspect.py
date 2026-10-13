"""Draws a recording as a picture, for checking cuts without listening: the waveform (with the found events marked:
green where one starts, red where it ends) above its spectrogram (0-24 kHz, linear).

    python -I art/audio/inspect.py <audio> <out.png> [--start s] [--duration s] [--steps | --events] [--width px]
    python -I art/audio/inspect.py --sheet <out.png> <audio> <audio> ...    (one strip per file, waveform only)
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import audiolib  # noqa: E402


def picture(path, out, start=0.0, duration=None, mode=None, width=1600):
    if duration is None:
        info = audiolib.run(["-i", path, "-f", "null", "-t", "0", "-"])
        import re
        m = re.search(r"Duration: (\d+):(\d+):([0-9.]+)", info)
        duration = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3)) - start
    events = []
    if mode == "steps":
        events = audiolib.find_steps(path, start, duration)
    elif mode == "events":
        events = audiolib.find_events(path, start, duration)
    boxes = []
    for e in events:
        x0 = int((e.start - start) / duration * width)
        x1 = int((e.end - start) / duration * width)
        boxes.append("drawbox=x=%d:y=0:w=2:h=ih:color=lime@0.9:t=fill" % x0)
        boxes.append("drawbox=x=%d:y=0:w=2:h=ih:color=red@0.9:t=fill" % x1)
    wave = "showwavespic=s=%dx220:split_channels=0:colors=0xd0d0d0:scale=sqrt" % width
    if boxes:
        wave += "," + ",".join(boxes)
    graph = ("[0:a]atrim=start=%f:duration=%f,asetpts=PTS-STARTPTS,aformat=channel_layouts=mono,asplit=2[a][b];"
             "[a]%s[w];[b]showspectrumpic=s=%dx240:legend=0:scale=log:color=intensity:fscale=lin[s];"
             "[w][s]vstack=inputs=2[v]" % (start, duration, wave, width - 0, ))
    # showspectrumpic's picture is wider than s by its frame; scale it to the waveform's width.
    graph = graph.replace("[s];[w][s]", "[s0];[s0]scale=%d:240[s];[w][s]" % width)
    audiolib.run(["-i", path, "-filter_complex", graph, "-map", "[v]", "-frames:v", "1", out])
    return events


def sheet(out, paths, width=1200, height=90):
    inputs = []
    parts = []
    for i, p in enumerate(paths):
        inputs += ["-i", p]
        parts.append("[%d:a]aformat=channel_layouts=mono,showwavespic=s=%dx%d:colors=0xd0d0d0:scale=sqrt,"
                     "drawbox=x=0:y=0:w=iw:h=1:color=0x404040:t=fill[w%d]" % (i, width, height, i))
    graph = ";".join(parts) + ";" + "".join("[w%d]" % i for i in range(len(paths))) + \
        "vstack=inputs=%d[v]" % len(paths) if len(paths) > 1 else ";".join(parts).replace("[w0]", "[v]")
    audiolib.run(inputs + ["-filter_complex", graph, "-map", "[v]", "-frames:v", "1", out])


if __name__ == "__main__":
    args = sys.argv[1:]
    if args and args[0] == "--sheet":
        sheet(args[1], args[2:])
        sys.exit(0)
    if len(args) < 2:
        print(__doc__)
        sys.exit(2)
    opts = {"start": 0.0, "duration": None, "mode": None, "width": 1600}
    i = 2
    while i < len(args):
        a = args[i]
        if a in ("--start", "--duration", "--width"):
            opts[a[2:]] = float(args[i + 1]) if a != "--width" else int(args[i + 1])
            i += 2
            continue
        if a in ("--steps", "--events"):
            opts["mode"] = a[2:]
        i += 1
    found = picture(args[0], args[1], opts["start"], opts["duration"], opts["mode"], opts["width"])
    for e in found:
        print(e)
