"""Where the sound pipeline's recordings come from (GROUND.md §9.2, game/assets/THIRD_PARTY.md).

Every download lives outside the repository in its own folder under D:\\GameDev\\Assets\\Downloads (set
HOLLOW_DOWNLOADS elsewhere) and is checked against its SHA-256 before use. Archives are unpacked once into
art/.cache/audio/src (not committed) by Python's zipfile and our own 7z reader; nothing inside them is ever run.

    python -I art/audio/sources.py --verify      checks every download that is present
    python -I art/audio/sources.py --fetch       downloads the missing ones (new folders), then verifies
"""

import hashlib
import json
import os
import sys
import urllib.request
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import sevenzip  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
DOWNLOADS = os.environ.get("HOLLOW_DOWNLOADS", "D:/GameDev/Assets/Downloads")
CACHE = os.path.join(REPO, "art", ".cache", "audio", "src")


class Pack:
    def __init__(self, title, file, url, page, author, licence, sha256, size):
        self.title, self.file, self.url, self.page = title, file, url, page
        self.author, self.licence, self.sha256, self.size = author, licence, sha256, size


KENNEY = "Kenney (kenney.nl)"
OGA = "https://opengameart.org/content/"
OGA_FILES = "https://opengameart.org/sites/default/files/"

PACKS = {
    "kenney-impact-sounds": Pack(
        "Impact Sounds", "kenney_impact-sounds.zip",
        "https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip",
        "https://kenney.nl/assets/impact-sounds", KENNEY, "CC0 1.0",
        "029d734af1582474edf3a694d1b0cebc97c1c152f2f39fa34d4c2bafc5de77f8", 800850),
    "kenney-rpg-audio": Pack(
        "RPG Audio", "kenney_rpg-audio.zip",
        "https://kenney.nl/media/pages/assets/rpg-audio/8e99002d76-1677590336/kenney_rpg-audio.zip",
        "https://kenney.nl/assets/rpg-audio", KENNEY, "CC0 1.0",
        "6dbeaf8544da958d8f2adcb4a4a4b76c1ade34a05f8ab9edccd327da7375f38b", 964837),
    "kenney-interface-sounds": Pack(
        "Interface Sounds", "kenney_interface-sounds.zip",
        "https://kenney.nl/media/pages/assets/interface-sounds/fa43c1dd4d-1677589452/kenney_interface-sounds.zip",
        "https://kenney.nl/assets/interface-sounds", KENNEY, "CC0 1.0",
        "f2193d072726d6758a5f7871b2dcc54dcce0d5c35c6f0a62f92549b327c81232", 834536),
    "kenney-ui-audio": Pack(
        "UI Audio", "kenney_ui-audio.zip",
        "https://kenney.nl/media/pages/assets/ui-audio/490d233f68-1677590494/kenney_ui-audio.zip",
        "https://kenney.nl/assets/ui-audio", KENNEY, "CC0 1.0",
        "946fc23a63d535d693eb31b2eabb80c8c28d6351e2186b344ceb71b2cb1d5eb6", 411949),
    "kenney-sci-fi-sounds": Pack(
        "Sci-fi Sounds", "kenney_sci-fi-sounds.zip",
        "https://kenney.nl/media/pages/assets/sci-fi-sounds/6b296f9ecf-1677589334/kenney_sci-fi-sounds.zip",
        "https://kenney.nl/assets/sci-fi-sounds", KENNEY, "CC0 1.0",
        "119340f351a5098ad814f78719438c0da355a9ce8a4c8a3af6a8d48aa3d49e04", 5875104),
    "oga-free-firearm-sound-library": Pack(
        "The Free Firearm Sound Library, prepared version: 22 firearms at near and mid distance, 96 kHz",
        "Prepared SFX Library.7z", OGA_FILES + "Prepared%20SFX%20Library.7z",
        OGA + "the-free-firearm-sound-library",
        "Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney (Still North Media)", "CC0 1.0",
        "cc1ab5a99a0a365105c7c5dd783f4b0b1fe90938114d3ceec53856bfe005f7d6", 193954738),
    "oga-haeldb-male-grunts": Pack(
        "Male Grunt/Yelling sounds: four voices, Neumann microphone", "yelling sounds.zip",
        OGA_FILES + "yelling%20sounds.zip", OGA + "male-gruntyelling-sounds",
        "HaelDB", "CC0 1.0 (the page offers OGA-BY 3.0 or CC0; used under CC0)",
        "e9100a4e3b9dcd146993089970dc6097dcf9935fa4683b196040012bad65d67a", 9715828),
    "oga-tinyworlds-different-steps": Pack(
        "Different steps on wood, stone, leaves, gravel and mud", "[kdd]DifferentSteps_0.zip",
        OGA_FILES + "%5Bkdd%5DDifferentSteps_0.zip",
        OGA + "different-steps-on-wood-stone-leaves-gravel-and-mud", "TinyWorlds", "CC0 1.0",
        "987d072b7deaa65177e005ddd8185f2351be7c9a76cd7575dea560549c39b33b", 77711),
    "oga-fantozzi-footsteps": Pack(
        "Fantozzi's Footsteps (Grass/Sand & Stone)", "Fantozzi-footsteps.7z", OGA_FILES + "Fantozzi-footsteps.7z",
        OGA + "fantozzis-footsteps-grasssand-stone", "Fantozzi (freesound.org), sliced by qubodup", "CC0 1.0",
        "415d360e0911c1c1c5355ee94023b772e6f4044f59b9b580610e7dbffd1840fd", 476363),
    "oga-corsica-snow-gravel": Pack(
        "42 Snow and Gravel Footsteps", "corsica_s-walking_in_snow.7z", OGA_FILES + "corsica_s-walking_in_snow.7z",
        OGA + "42-snow-and-gravel-footsteps", "Corsica_S (freesound.org), sliced by qubodup", "CC0 1.0",
        "206306b51f8eeedf82043d6628174ec8ff9b16230dffb5192a5c74688e62abd4", 4041779),
    "oga-rubberduck-metal-wood": Pack(
        "100 CC0 metal and wood SFX", "100-CC0-wood-metal-SFX.zip", OGA_FILES + "100-CC0-wood-metal-SFX.zip",
        OGA + "100-cc0-metal-and-wood-sfx", "rubberduck", "CC0 1.0",
        "be6eba63b03409ac0c77787a956b1503a7c186403d04aef9725c52644a4b7878", 1990361),
    "oga-rubberduck-breaking": Pack(
        "75 CC0 breaking / falling / hit sfx", "sfx_breaking_and_falling.zip",
        OGA_FILES + "sfx_breaking_and_falling.zip", OGA + "75-cc0-breaking-falling-hit-sfx", "rubberduck", "CC0 1.0",
        "e6ee04d91c5f4d30cfda1260d2c9d1faf96fda36319287215fbd07bcb1a80451", 1624406),
}

# BigSoundBank (https://bigsoundbank.com): one WAV per sound by Joseph Sardin. Its licence page lists CC0 1.0 (also
# WTFPL and a public-domain dedication) and says nothing is mandatory. Folder "bigsoundbank", files <id>.wav from
# https://bigsoundbank.com/UPLOAD/bwf-en/<id>.wav; each sound's page is https://bigsoundbank.com/s<id>.html.
BIGSOUNDBANK_AUTHOR = "Joseph Sardin (BigSoundBank.com)"
with open(os.path.join(HERE, "bigsoundbank.json"), encoding="utf-8") as _f:
    BIGSOUNDBANK = json.load(_f)

USED = set()


def pack(name, member):
    """The path of a file inside a pack, unpacked into the cache on first use."""
    p = PACKS[name]
    root = os.path.join(CACHE, name)
    if not os.path.isdir(root):
        archive = os.path.join(DOWNLOADS, name, p.file)
        if not os.path.isfile(archive):
            raise SystemExit("missing download %s (python -I art/audio/sources.py --fetch)" % archive)
        check(archive, p.sha256)
        part = root + ".part"
        os.makedirs(part, exist_ok=True)
        if p.file.endswith(".zip"):
            with zipfile.ZipFile(archive) as z:
                for m in z.infolist():
                    if m.is_dir():
                        continue
                    path = sevenzip.safe(os.path.realpath(part), m.filename)
                    os.makedirs(os.path.dirname(path), exist_ok=True)
                    with z.open(m) as i, open(path, "wb") as o:
                        o.write(i.read())
        else:
            sevenzip.Archive(archive).extract(part)
        os.replace(part, root)
    path = os.path.join(root, *member.split("/"))
    if not os.path.isfile(path):
        raise SystemExit("%s has no %s" % (name, member))
    USED.add((name, member))
    return path


def bsb(sid):
    """The path of a BigSoundBank recording by its id ("0705")."""
    if sid not in BIGSOUNDBANK:
        raise SystemExit("unknown BigSoundBank id %s" % sid)
    path = os.path.join(DOWNLOADS, "bigsoundbank", sid + ".wav")
    if not os.path.isfile(path):
        raise SystemExit("missing download %s (python -I art/audio/sources.py --fetch)" % path)
    check(path, BIGSOUNDBANK[sid]["sha256"])
    USED.add(("bigsoundbank", sid))
    return path


_checked = set()


def check(path, sha256):
    if path in _checked:
        return
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    if h.hexdigest() != sha256:
        raise SystemExit("%s: SHA-256 %s, expected %s" % (path, h.hexdigest(), sha256))
    _checked.add(path)


def verify(fetch=False):
    missing = 0
    items = [(os.path.join(DOWNLOADS, n, p.file), p.url, p.sha256) for n, p in PACKS.items()]
    items += [(os.path.join(DOWNLOADS, "bigsoundbank", sid + ".wav"),
               "https://bigsoundbank.com/UPLOAD/bwf-en/%s.wav" % sid, v["sha256"]) for sid, v in BIGSOUNDBANK.items()]
    for path, url, sha in items:
        if not os.path.isfile(path):
            if not fetch:
                missing += 1
                continue
            os.makedirs(os.path.dirname(path), exist_ok=True)
            req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (Hollow Universe sound pipeline)"})
            with urllib.request.urlopen(req, timeout=300) as r, open(path + ".part", "wb") as o:
                o.write(r.read())
            os.replace(path + ".part", path)
            print("fetched", path)
        check(path, sha)
    print("%d downloads checked, %d missing" % (len(items) - missing, missing))
    return missing == 0


if __name__ == "__main__":
    if "--fetch" in sys.argv:
        verify(fetch=True)
    elif "--verify" in sys.argv:
        sys.exit(0 if verify() else 1)
    else:
        print(__doc__)
