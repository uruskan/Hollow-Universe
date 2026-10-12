"""Sets Godot import options for the character and item assets (run with plain Python after Godot has created the
.import files once, e.g. after `godot --headless --path game --import`, then import again):

    python -I art/lib/import_settings.py

- Textures used on 3D models: VRAM compressed with mipmaps; normal maps compressed as normal maps.
- Character and item models: no automatic levels of detail (the builds export their own), no light baking.
- Animations: constant tracks are kept, because a clip that holds a pose still has to win a blend.
"""
import os
import re
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FOLDERS = ["game/assets/characters", "game/assets/items"]


def set_params(path, values):
    with open(path, encoding="utf-8") as f:
        text = f.read()
    changed = text
    for key, value in values.items():
        pattern = re.compile(r"^%s=.*$" % re.escape(key), re.M)
        if pattern.search(changed):
            changed = pattern.sub("%s=%s" % (key, value), changed)
        else:
            changed = changed.rstrip("\n") + "\n%s=%s\n" % (key, value)
    if changed != text:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(changed)
        return True
    return False


def main():
    count = 0
    for folder in FOLDERS:
        for root, _, files in os.walk(os.path.join(REPO, folder)):
            for name in files:
                if not name.endswith(".import"):
                    continue
                path = os.path.join(root, name)
                source = name[: -len(".import")]
                if source.endswith(".png") or source.endswith(".jpg"):
                    values = {"compress/mode": "2", "mipmaps/generate": "true", "detect_3d/compress_to": "0"}
                    values["compress/normal_map"] = "1" if "normal" in source else "2"
                elif source.endswith(".glb"):
                    values = {"meshes/generate_lods": "false", "meshes/light_baking": "0",
                              "animation/remove_immutable_tracks": "false"}
                else:
                    continue
                if set_params(path, values):
                    count += 1
    print("import settings updated in %d files" % count)


if __name__ == "__main__":
    sys.exit(main())
