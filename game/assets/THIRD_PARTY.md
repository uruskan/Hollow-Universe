# Third-party material

Everything in `game/assets/` that is not our own work, where it came from and under what licence (GROUND.md §9).
Downloads live outside the repository in `D:\GameDev\Assets\Downloads\<name>\`; the scripts in `art/` read them from
there. Plants, rocks, grass, foliage and bark textures are made in code
(`game/scripts/Surface/Flora`, GROUND.md §4.6) and are not listed.

## Fonts

| What | Source | Licence |
|---|---|---|
| Inter (variable), `fonts/Inter/` | https://github.com/rsms/inter | SIL Open Font License 1.1 (`fonts/Inter/OFL.txt`) |
| Barlow Medium and SemiBold, Barlow Condensed Medium and Bold: the ground interface's fonts, as in RustMVP, `fonts/Barlow/` | [jpt/barlow](https://github.com/jpt/barlow), via RustMVP `assets/SourceUI/fonts` | SIL OFL 1.1 (`fonts/Barlow/OFL.txt`) |

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

## Held items (`items/`)

Not downloads: kits made for this project by Blender Python (GROUND.md §9). `art/items/build_items.py` reads their
source `.blend` files and renames the items.

| Ours | Source | Licence of the textures |
|---|---|---|
| Flint axe, flint pick, hand stone, Wren pistol; `items/textures/psg_surface_*.png` (the kit's shared atlas, 4096 → 2048 px) | `D:\GameDev\Assets\PSG_StarterKit_R02` (stone axe, stone pickaxe, rock, P250-style pistol) | Three atlas tiles are modified CC0 Poly Haven textures by Rob Tuytel (fine_grained_wood, rusty_metal_02, green_metal_rust); the rest is the kit's own |
| Kestrel rifle; `items/textures/kestrel_*.png` (4096 → 2048 px) | `D:\GameDev\Projects\RustMVP\assets\PSG_FrontierAK_R05` (static meshes LOD1–LOD3, sockets, aiming camera) | The kit's own bakes; its wood is Poly Haven's rough_wood (CC0; licence and download record in the kit's `Source/ThirdParty/rough_wood`) |

## Ground textures (`surface/ground/`)

`ground_albedo.webp` and `ground_normal.webp` are two atlases of eight 2048 px layers each (base colour; normal and
height), built by `tools/blender/surface_ground.py` from the sets below: downscaled from 4K, packed, normal maps
brought to one convention. Layers in `Hollow.Surface.GroundMaterial` order:

| Layer | Set | Source | Licence |
|---|---|---|---|
| 0 grass (recoloured by the shader) | Dry Grassy Ground (Quixel Megascans, `vbmkebz`) | https://www.fab.com/listings/4db25ac9-6a02-43b9-a299-f36d99dd50c7 | Fab Standard License |
| 1 forest floor | Forest Floor (Quixel Megascans, `vmcjadofw`) | https://www.fab.com/listings/012dd765-a189-429c-986f-a52f0a1a6453 | Fab Standard License |
| 2 soil | `dirt_floor` (Poly Haven, from the "Verdant Trail" scene pack) | https://polyhaven.com/a/dirt_floor | CC0 |
| 3 rock (also the boulders' texture) | Granite Rock (Quixel Megascans, `wgpqfg3`) | https://www.fab.com/listings/67d78d30-4ecc-4d89-8ee1-eace8c7379b5 | Fab Standard License |
| 4 gravel | Gravel Ground (Quixel Megascans, `vixscfddw`) | https://www.fab.com/listings/572592ac-0f9f-4789-bb8a-60431e08c483 | Fab Standard License |
| 5 sand | Beach Sand (Quixel Megascans, `ukviejtlw`) | https://www.fab.com/listings/2342785d-12a5-465d-a74a-4ce98909aaed | Fab Standard License |
| 6 snow | Clean Snow (Quixel Megascans, `ueqmfe3fw`) | https://www.fab.com/listings/a8080c7e-824d-4abb-a980-05f23aafd902 | Fab Standard License |
| 7 moss (recoloured by the shader) | `forest_ground_04` (Poly Haven, from the "Pine Forest" scene pack) | https://polyhaven.com/a/forest_ground_04 | CC0 |

The Megascans sets are owned on the project's Fab account (listing URLs from the local download manifest,
`D:\GameDev\Assets\Bare-World-Manual\DOWNLOAD-MANIFEST-VERIFIED.json`). The Fab Standard License allows use in any
engine; none of these listings is one of the Unreal-Engine-only ones (to be re-checked on the listing pages, GROUND.md
§11). The Fab Standard License does not allow redistributing the files on their own: they ship only inside the game.
