"""Make a labelled-by-time A/B listening reel from two built footstep directories.

    python -I art/audio/footstep_comparison.py --before DIR --after DIR --out DIR

This is a sample comparison, NOT a gameplay recording or a listening verdict. It uses
the same variant order/pitch/gain in both halves. The hosted audio-movement tour is
the separate check of real input, cadence, contact selection and the engine mix.
"""
import argparse
import array
import hashlib
import inspect  # Load stdlib before this directory's inspect.py can shadow it.
import json
import os
import sys
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import audiolib


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--before", required=True)
    parser.add_argument("--after", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)
    rate = 48000
    pcm = array.array("h")
    timeline = []
    # Catalogue gain + the normal local -3 dB; landing uses a representative
    # 5 m/s fall (-4.05 dB cadence gain). No extra loudness match or processing.
    cases = [("grass", "walk", 4, .55, -3), ("dirt", "walk", 4, .55, -3),
             ("grass", "land", 3, 1.15, -3.05), ("dirt", "land", 3, 1.15, -3.05)]
    for surface, gait, count, gap, gain in cases:
        for label, root in (("before", args.before), ("after", args.after)):
            start = len(pcm) / rate
            track = [0.0] * round((count * gap + 0.4) * rate)
            hashes = {}
            for i in range(count):
                rel = f"{surface}/{gait}_{i+1:02}.ogg"
                path = os.path.join(root, rel)
                with open(path, "rb") as f:
                    hashes[rel] = hashlib.sha256(f.read()).hexdigest()
                samples = audiolib.decode(path, rate)
                offset = round(i * gap * rate)
                scale = 10 ** (gain / 20)
                for j, value in enumerate(samples[:len(track)-offset]):
                    track[offset+j] += value * scale
            peak = max(abs(x) for x in track)
            if peak >= 1:
                raise SystemExit(f"{label} {surface}/{gait} clips: {peak}")
            pcm.extend(round(x * 32767) for x in track)
            timeline.append(dict(start=round(start, 3), end=round(len(pcm)/rate, 3),
                                 version=label, event=f"footstep.{gait}.{surface}",
                                 gainDb=gain, intervalSeconds=gap, sha256=hashes))
            pcm.extend([0] * rate)
    wav = os.path.join(args.out, "footsteps-before-after.wav")
    with wave.open(wav, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(rate)
        f.writeframes(pcm.tobytes())
    with open(os.path.join(args.out, "footsteps-before-after.json"), "w", encoding="utf-8") as f:
        json.dump(dict(kind="sample comparison; not gameplay", pitch=1, jitterDb=0,
                       listeningAcceptance="pending", timeline=timeline), f, indent=2)
        f.write("\n")
    print(wav)
    for item in timeline:
        print(f"{item['start']:5.2f}-{item['end']:5.2f}s {item['version']:6} {item['event']}")


if __name__ == "__main__":
    main()
