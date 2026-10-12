# Third-party material

Everything in `game/assets/` that is not our own work, where it came from and under what licence (GROUND.md §9).
Downloads live outside the repository in `D:\GameDev\Assets\Downloads\<name>\`; the scripts in `art/` read them from
there.

## Fonts

| What | Source | Licence |
|---|---|---|
| Inter (variable), `fonts/Inter/` | https://github.com/rsms/inter | SIL Open Font License 1.1 (`fonts/Inter/OFL.txt`) |

## Characters (`characters/`)

Downloaded on 12 October 2026 with the owner's approval.

| Download | Version | Size | SHA-256 | Licence | Used for |
|---|---|---|---|---|---|
| Quaternius, Universal Animation Library, Standard: `universal_animation_librarystandard.zip`, https://opengameart.org/content/universal-animation-library (also https://quaternius.itch.io/universal-animation-library) | Standard, files dated March 2025 | 14,541,205 bytes | `18ff1a7215f4852b320203e8aaf02a1578b5c8eef9027fbaedfcedc7b85a3ac2` | CC0 1.0 | Clips retargeted into `animations.glb` (`animations.json` names each clip's source) |
| Quaternius, Universal Animation Library 2, Standard: `universal_animation_library_2standard.zip`, https://opengameart.org/content/universal-animation-library-2 (also https://quaternius.itch.io/universal-animation-library-2) | Standard, files dated January 2026 | 10,257,419 bytes | `ec0e40d6d78fe9aaad59e322f40865a8675c22f0745e291622e54520391a9217` | CC0 1.0 | Clips retargeted into `animations.glb` |
| MPFB, MakeHuman for Blender: `add-on-mpfb-v2.0.17.zip`, https://extensions.blender.org/add-ons/mpfb/ | 2.0.17 (22 July 2026) | 45,031,536 bytes (42.9 MB) | `4f0a879d64a39bf646fbf5f53601ac678855da329d650617dca5737548239a87` | Add-on code GPL-3.0-or-later (a tool, not shipped); the MakeHuman base mesh, targets and weights it applies are CC0 1.0 | Builds the body and its skeleton and weights (`human_male.glb`) |
| MakeHuman system assets: `makehuman_system_assets_cc0.zip`, https://files2.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip | Files dated April 2024 | 280,737,770 bytes | `b542127a8e25547c7c29c19f2d1d2adb9a664c80396ecd694095dbc8028a0107` | CC0 1.0 | Skin `young_caucasian_male`, eyes (high-poly, brown), eyebrow001, eyelashes01, hair short02 and their textures (`characters/textures/skin_male_albedo.png`, `eye_albedo.png`, `brow_albedo.png`, `lash_albedo.png`, `hair_albedo.png`, `hair_normal.png`) |

Our own work in `characters/`: the body's regions, underwear and levels of detail, every garment and its textures, the
procedural surface textures (`*_detail_normal.png`, `knit_normal.png`, `weave_normal.png` and the like), and the
retargeting, all made by the scripts in `art/`.
