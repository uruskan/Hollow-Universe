"""Install the owner's reviewed-source R01 pickups/containers into this project.

Checks the supplied SHA-256 manifests before copying; only resource paths in the
native Godot scenes/materials change. This is a local integration receipt, not a
game-dev canonical package or an assertion of artistic approval.
"""
import hashlib
import json
from pathlib import Path
import re
import shutil

ROOT = Path(__file__).resolve().parents[1]
ASSETS = Path("D:/GameDev/Assets")
PACKAGES = {
    "Hollow_Salvage_R01": ["gears", "springs", "metal-pipe", "sheet-metal", "tech-scrap", "rope"],
    "Hollow_LootContainers_R01": ["loot-crate", "military-crate", "loot-barrel-1"],
}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    catalog_path = ROOT / "game/data/ground/models.json"
    catalog = catalog_path.read_text(encoding="utf-8")
    receipt = {"schema": 1, "method": "Verified local Godot scene integration", "packages": []}
    for package, keys in PACKAGES.items():
        source = ASSETS / package
        manifest_path = source / "Reports/package_manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        roster = {entry["path"]: entry for entry in manifest["files"]}
        # Verify the entire published roster, not just the files selected below.
        for relative, entry in roster.items():
            path = source / relative
            if not path.is_file() or digest(path) != entry["sha256"]:
                raise ValueError(f"Source manifest mismatch: {path}")
        destination = ROOT / "game/assets/revised" / package
        prefix = f"res://assets/revised/{package}/"
        selected = [f"Scenes/{key}.tscn" for key in keys]
        selected += [p for p in roster if p.startswith("Materials/") and p.endswith(".tres")]
        selected += [p for p in roster if p.startswith("Textures/") and p.endswith(".png") and "Checker" not in p]
        selected += ["HANDOFF.json", "README.md", "Reports/provenance.json", "Reports/package_manifest.json"]
        entry = {"source": str(source), "source_manifest_sha256": digest(manifest_path),
                 "verified_source_files": len(roster), "models": keys, "files": []}
        for relative in selected:
            src = source / relative
            dst = destination / relative
            dst.parent.mkdir(parents=True, exist_ok=True)
            if dst.suffix in (".tscn", ".tres"):
                dst.write_text(src.read_text(encoding="utf-8-sig").replace("res://", prefix), encoding="utf-8")
            else:
                shutil.copyfile(src, dst)
            entry["files"].append({"source": relative, "destination": dst.relative_to(ROOT).as_posix(),
                                   "source_sha256": digest(src), "installed_sha256": digest(dst)})
        for key in keys:
            # These entries have no nested object beyond icon/gibs; stop at the next model key.
            pattern = rf'(    \{{ "key": "{re.escape(key)}",.*?)(?=\n    \{{ "key"|\n\s*\])'
            matches = list(re.finditer(pattern, catalog, flags=re.S))
            if len(matches) != 1:
                raise ValueError(f"Expected one catalog entry for {key}")
            old = matches[0].group(1)
            new = re.sub(r'"file": "res://[^\"]+"', f'"file": "{prefix}Scenes/{key}.tscn"', old, count=1)
            new = re.sub(r'"standIn": "[^\"]+", ', '', new)
            new = re.sub(r'"source": "[^\"]+"', f'"source": "{package}: original owner-controlled work; verified local integration receipt in assets/revised/integration.json"', new)
            if package == "Hollow_Salvage_R01":
                new = new.replace('"source":', '"icon": {}, "source":') if '"icon":' not in new else new
                for size in (128, 256):
                    relative = f"Icons/{size}/{key}.png"
                    src = source / relative
                    dst = ROOT / f"game/assets/items/icons/{size}/{key}.png"
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(src, dst)
                    entry["files"].append({"source": relative, "destination": dst.relative_to(ROOT).as_posix(),
                                           "source_sha256": digest(src), "installed_sha256": digest(dst)})
            catalog = catalog.replace(old, new, 1)
        receipt["packages"].append(entry)
    # Ensure that a native-resource path accidentally left at package root never ships.
    for package in PACKAGES:
        for path in (ROOT / "game/assets/revised" / package).rglob("*"):
            if path.suffix not in (".tres", ".tscn"):
                continue
            for relative in re.findall(r'path="(res://[^\"]+)"', path.read_text(encoding="utf-8")):
                if not (ROOT / "game" / relative[6:]).is_file():
                    raise ValueError(f"Broken resource reference in {path}: {relative}")
    catalog_path.write_text(catalog, encoding="utf-8")
    (ROOT / "game/assets/revised/integration.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(f"Integrated {sum(map(len, PACKAGES.values()))} models and 12 icons; full source rosters verified.")


if __name__ == "__main__":
    main()
