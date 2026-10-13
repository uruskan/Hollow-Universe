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
engine. **Checked on 2026-10-13:** each of the six listing pages above gives "License terms: Standard License" and
none of them says the product is licensed for use only with Unreal Engine (the pages mention Unreal only in Fab's
footer). The Fab Standard License does not allow redistributing the files on their own: they ship only inside the
game.

## Item and world models (`items/`, `world/`, `textures/`, `materials/`)

The models come from kits made for this project by Blender Python (the PSG kits and the Tin Finch, in
`D:\GameDev\Assets` and `D:\GameDev\Projects\RustMVP\assets`) or from our own scripts in `art/` (GROUND.md §9.1). No
model, texture or animation from another game is used. The textures below hold third-party photographs; everything
else in them was made for the project.

| Path | What | Source | Licence |
|---|---|---|---|
| `textures/psg/` | The PSG Starter Kit R02 shared atlas (4096 → 2048 px), used by the Starter Kit items and by our own models. Tiles 0, 1 and 5 are modified photographic textures by Rob Tuytel: fine_grained_wood, rusty_metal_02, green_metal_rust; the other tiles are the kit's own. Record: the kit's `Reports/texture_provenance.json` and `THIRD_PARTY_NOTICES.md` | https://polyhaven.com/a/fine_grained_wood, https://polyhaven.com/a/rusty_metal_02, https://polyhaven.com/a/green_metal_rust | CC0 1.0 (https://polyhaven.com/license) |
| `textures/frontier/` | The PSG Frontier AK R05 bakes (4096 → 2048 px), used by the rifle. The wood is modified from Poly Haven's rough_wood by Rob Tuytel; the metal is the kit's own. Record: the kit's `Reports/material_provenance.json` and `Source/ThirdParty/rough_wood/LICENSE.txt` | https://polyhaven.com/a/rough_wood | CC0 1.0 |
| `textures/rock/` | A neutral rock albedo, roughness and OpenGL normal map (1024 px, tiling), made by `art/hollow/build.py` from Poly Haven's rock_08 (diffuse, roughness and displacement at 4K, as downloaded with the PSG Resource Nodes kit: `ThirdParty/PolyHaven/rock_08`). Used by the outcrops, stones, ore, bark and the furnace | https://polyhaven.com/a/rock_08 | CC0 1.0 |
| `textures/tin-finch/` | The Tin Finch's surface and markings atlases (2048 and 1024 px) | Tin Finch TF-01, generated for the project (its `README.md`: no third-party textures) | Ours |
| `items/`, `world/` | GLB models: the PSG Starter Kit R02 items and the Frontier AK R05 (converted, renamed), the Tin Finch, and our own models | As above | Ours (no third-party geometry) |
| `items/icons/` | Item icons, rendered from the models by `--view icons` | Ours | Ours |
| `building/` (GLB) | The building kit's 13 pieces in 5 grades and the door and window kit's doors, garage door, bars, glass and embrasures, converted by `art/import/`; 7 more pieces built by the same scripts with the kit's own generator | `D:\GameDev\Assets\PSG_BuildingKit_R01`, `PSG_DoorWindowKit_R01`: our own kits, made by Blender Python (GROUND.md §9) | Ours |
| `building/textures/` | The kits' surface atlases, shrunk to 2K: Poly Haven's weathered brown planks, brown bark 02, rock surface, rusty metal sheet and painted concrete, as the kits' `texture_manifest.json` and `source_texture_provenance.json` record | [Poly Haven](https://polyhaven.com) | CC0 1.0 |

## Sounds (`audio/`)

Downloaded on 13 October 2026 with the owner's approval of free CC0 downloads, each into its own folder under
`D:\GameDev\Assets\Downloads\<name>\` (BigSoundBank's single files all into `bigsoundbank\`). `art/audio/build.py`
cuts, layers, filters and levels them into `audio/` (GROUND.md §4.9, §9.2); `art/audio/sources.py` lists every
download with its address and SHA-256 and checks them before use (`--fetch` downloads what is missing). No sound from
Rust or any other game is used. The tables are written by `python -I art/audio/build.py --credits`: every pack and
every BigSoundBank recording the recipes use, and only those.

Made in code, ours: the calm and high wind beds, the outdoor impulse response of the far gunshot tails, and the code
lock's and the charge timer's piezo tones (`art/audio/synth.py`).

| Download | Licence | SHA-256 | Used for |
|---|---|---|---|
| Kenney (kenney.nl), Impact Sounds: `kenney_impact-sounds.zip`, https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip (page: https://kenney.nl/assets/impact-sounds), 800,850 bytes | CC0 1.0 | `029d734af1582474edf3a694d1b0cebc97c1c152f2f39fa34d4c2bafc5de77f8` | 54 files: `impactGlass_light_000.ogg`, `impactGlass_light_001.ogg`, `impactMetal_heavy_000.ogg`, `impactMetal_heavy_001.ogg`, `impactMetal_heavy_002.ogg`, `impactMetal_light_000.ogg`, `impactMetal_light_001.ogg`, `impactMetal_light_002.ogg`, `impactMetal_light_003.ogg`, `impactMining_000.ogg`, `impactMining_001.ogg`, `impactMining_002.ogg` ... |
| Kenney (kenney.nl), Interface Sounds: `kenney_interface-sounds.zip`, https://kenney.nl/media/pages/assets/interface-sounds/fa43c1dd4d-1677589452/kenney_interface-sounds.zip (page: https://kenney.nl/assets/interface-sounds), 834,536 bytes | CC0 1.0 | `f2193d072726d6758a5f7871b2dcc54dcce0d5c35c6f0a62f92549b327c81232` | 9 files: `bong_001.ogg`, `confirmation_001.ogg`, `confirmation_002.ogg`, `drop_002.ogg`, `error_006.ogg`, `error_008.ogg`, `question_002.ogg`, `switch_002.ogg`, `switch_004.ogg` |
| Kenney (kenney.nl), RPG Audio: `kenney_rpg-audio.zip`, https://kenney.nl/media/pages/assets/rpg-audio/8e99002d76-1677590336/kenney_rpg-audio.zip (page: https://kenney.nl/assets/rpg-audio), 964,837 bytes | CC0 1.0 | `6dbeaf8544da958d8f2adcb4a4a4b76c1ade34a05f8ab9edccd327da7375f38b` | 17 files: `beltHandle1.ogg`, `cloth1.ogg`, `cloth2.ogg`, `cloth3.ogg`, `cloth4.ogg`, `clothBelt.ogg`, `clothBelt2.ogg`, `creak1.ogg`, `creak2.ogg`, `doorClose_1.ogg`, `doorClose_2.ogg`, `doorClose_4.ogg` ... |
| Kenney (kenney.nl), Sci-fi Sounds: `kenney_sci-fi-sounds.zip`, https://kenney.nl/media/pages/assets/sci-fi-sounds/6b296f9ecf-1677589334/kenney_sci-fi-sounds.zip (page: https://kenney.nl/assets/sci-fi-sounds), 5,875,104 bytes | CC0 1.0 | `119340f351a5098ad814f78719438c0da355a9ce8a4c8a3af6a8d48aa3d49e04` | 7 files: `explosionCrunch_000.ogg`, `explosionCrunch_001.ogg`, `explosionCrunch_002.ogg`, `explosionCrunch_003.ogg`, `explosionCrunch_004.ogg`, `lowFrequency_explosion_000.ogg`, `lowFrequency_explosion_001.ogg` |
| Kenney (kenney.nl), UI Audio: `kenney_ui-audio.zip`, https://kenney.nl/media/pages/assets/ui-audio/490d233f68-1677590494/kenney_ui-audio.zip (page: https://kenney.nl/assets/ui-audio), 411,949 bytes | CC0 1.0 | `946fc23a63d535d693eb31b2eabb80c8c28d6351e2186b344ceb71b2cb1d5eb6` | 5 files: `click1.ogg`, `click3.ogg`, `click5.ogg`, `rollover2.ogg`, `rollover4.ogg` |
| Corsica_S (freesound.org), sliced by qubodup, 42 Snow and Gravel Footsteps: `corsica_s-walking_in_snow.7z`, https://opengameart.org/sites/default/files/corsica_s-walking_in_snow.7z (page: https://opengameart.org/content/42-snow-and-gravel-footsteps), 4,041,779 bytes | CC0 1.0 | `206306b51f8eeedf82043d6628174ec8ff9b16230dffb5192a5c74688e62abd4` | 6 files: `Corsica_S-Walking_on_snow_covered_gravel_01.flac`, `Corsica_S-Walking_on_snow_covered_gravel_03.flac`, `Corsica_S-Walking_on_snow_covered_gravel_05.flac`, `Corsica_S-Walking_on_snow_covered_gravel_07.flac`, `Corsica_S-Walking_on_snow_covered_gravel_09.flac`, `Corsica_S-Walking_on_snow_covered_gravel_11.flac` |
| Fantozzi (freesound.org), sliced by qubodup, Fantozzi's Footsteps (Grass/Sand & Stone): `Fantozzi-footsteps.7z`, https://opengameart.org/sites/default/files/Fantozzi-footsteps.7z (page: https://opengameart.org/content/fantozzis-footsteps-grasssand-stone), 476,363 bytes | CC0 1.0 | `415d360e0911c1c1c5355ee94023b772e6f4044f59b9b580610e7dbffd1840fd` | 12 files: `Fantozzi-SandL1.flac`, `Fantozzi-SandL2.flac`, `Fantozzi-SandL3.flac`, `Fantozzi-SandR1.flac`, `Fantozzi-SandR2.flac`, `Fantozzi-SandR3.flac`, `Fantozzi-StoneL1.flac`, `Fantozzi-StoneL2.flac`, `Fantozzi-StoneL3.flac`, `Fantozzi-StoneR1.flac`, `Fantozzi-StoneR2.flac`, `Fantozzi-StoneR3.flac` |
| Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney (Still North Media), The Free Firearm Sound Library, prepared version: 22 firearms at near and mid distance, 96 kHz: `Prepared SFX Library.7z`, https://opengameart.org/sites/default/files/Prepared%20SFX%20Library.7z (page: https://opengameart.org/content/the-free-firearm-sound-library), 193,954,738 bytes | CC0 1.0 | `cc1ab5a99a0a365105c7c5dd783f4b0b1fe90938114d3ceec53856bfe005f7d6` | 8 files: `A_34P.wav`, `A_42P.wav`, `C_28P.wav`, `C_31P.wav`, `U_14P.wav`, `U_19P.wav`, `X_31P.wav`, `X_39P.wav` |
| HaelDB, Male Grunt/Yelling sounds: four voices, Neumann microphone: `yelling sounds.zip`, https://opengameart.org/sites/default/files/yelling%20sounds.zip (page: https://opengameart.org/content/male-gruntyelling-sounds), 9,715,828 bytes | CC0 1.0 (the page offers OGA-BY 3.0 or CC0; used under CC0) | `e9100a4e3b9dcd146993089970dc6097dcf9935fa4683b196040012bad65d67a` | 9 files: `3grunt1.wav`, `3grunt2.wav`, `3grunt3.wav`, `3grunt4.wav`, `3grunt5.wav`, `3grunt6.wav`, `3yell3.wav`, `3yell4.wav`, `3yell5.wav` |
| rubberduck, 75 CC0 breaking / falling / hit sfx: `sfx_breaking_and_falling.zip`, https://opengameart.org/sites/default/files/sfx_breaking_and_falling.zip (page: https://opengameart.org/content/75-cc0-breaking-falling-hit-sfx), 1,624,406 bytes | CC0 1.0 | `e6ee04d91c5f4d30cfda1260d2c9d1faf96fda36319287215fbd07bcb1a80451` | 14 files: `bfh1_falling_01.ogg`, `bfh1_falling_02.ogg`, `bfh1_falling_03.ogg`, `bfh1_rock_breaking_01.ogg`, `bfh1_rock_breaking_02.ogg`, `bfh1_rock_breaking_03.ogg`, `bfh1_rock_falling_02.ogg`, `bfh1_rock_falling_03.ogg`, `bfh1_rock_falling_04.ogg`, `bfh1_rock_falling_05.ogg`, `bfh1_rock_falling_06.ogg`, `bfh1_wood_breaking_01.ogg` ... |
| rubberduck, 100 CC0 metal and wood SFX: `100-CC0-wood-metal-SFX.zip`, https://opengameart.org/sites/default/files/100-CC0-wood-metal-SFX.zip (page: https://opengameart.org/content/100-cc0-metal-and-wood-sfx), 1,990,361 bytes | CC0 1.0 | `be6eba63b03409ac0c77787a956b1503a7c186403d04aef9725c52644a4b7878` | 16 files: `hammer_01.ogg`, `metal_close_01.ogg`, `metal_open_01.ogg`, `metal_slam_01.ogg`, `wood_breaking_01.ogg`, `wood_breaking_02.ogg`, `wood_cracking_01.ogg`, `wood_cracking_02.ogg`, `wood_cracking_03.ogg`, `wood_falling_01.ogg`, `wood_falling_02.ogg`, `wood_falling_03.ogg` ... |
| TinyWorlds, Different steps on wood, stone, leaves, gravel and mud: `[kdd]DifferentSteps_0.zip`, https://opengameart.org/sites/default/files/%5Bkdd%5DDifferentSteps_0.zip (page: https://opengameart.org/content/different-steps-on-wood-stone-leaves-gravel-and-mud), 77,711 bytes | CC0 1.0 | `987d072b7deaa65177e005ddd8185f2351be7c9a76cd7575dea560549c39b33b` | 3 files: `wood01.ogg`, `wood02.ogg`, `wood03.ogg` |

| BigSoundBank id | Title | File | SHA-256 |
|---|---|---|---|
| [0004](https://bigsoundbank.com/s0004.html) | Match #1 | https://bigsoundbank.com/UPLOAD/bwf-en/0004.wav (4,009,176 bytes) | `4177c90feea9f4b59db4b24a67f8ac99459b4c054b482caa08fb768e5020b0ab` |
| [0005](https://bigsoundbank.com/s0005.html) | Nail and Hammer #1 | https://bigsoundbank.com/UPLOAD/bwf-en/0005.wav (918,816 bytes) | `50b65b95663a4a0bd29e8baf27df24fd88fc29f856d4e4f750eca98204b21038` |
| [0006](https://bigsoundbank.com/s0006.html) | Nail and hammer #2 | https://bigsoundbank.com/UPLOAD/bwf-en/0006.wav (637,378 bytes) | `88a4244edacc13f7848b315b022948b118370cccd321971d2581ee5b2fe8e87c` |
| [0013](https://bigsoundbank.com/s0013.html) | Cloth | https://bigsoundbank.com/UPLOAD/bwf-en/0013.wav (868,366 bytes) | `7427de6e911d52a966c8a1106c160358f8703e71689637b7e86add909a8ff91e` |
| [0037](https://bigsoundbank.com/s0037.html) | Interior door lock | https://bigsoundbank.com/UPLOAD/bwf-en/0037.wav (223,100 bytes) | `3dd0d8c535beab13a9dc687d75640815e785f7bb46e762b7166bec99d63a5435` |
| [0146](https://bigsoundbank.com/s0146.html) | Strong Wind #1 | https://bigsoundbank.com/UPLOAD/bwf-en/0146.wav (6,929,366 bytes) | `9b62b250ca33821b2f26b1f16f48cef813a9646f476f2c4854766fa25928b808` |
| [0165](https://bigsoundbank.com/s0165.html) | Man Footsteps on the Wooden Floor | https://bigsoundbank.com/UPLOAD/bwf-en/0165.wav (763,722 bytes) | `ed4009d9d94b0aa8d87b2ec6209c72edbe8d1ae45b667f2e9a723d3096addad8` |
| [0191](https://bigsoundbank.com/s0191.html) | Lock | https://bigsoundbank.com/UPLOAD/bwf-en/0191.wav (827,900 bytes) | `3cb87357bddeda17622aca3501a28a85bac94c40cf48d51aaaae394d63a78106` |
| [0234](https://bigsoundbank.com/s0234.html) | Iron bar falling #1 | https://bigsoundbank.com/UPLOAD/bwf-en/0234.wav (73,432 bytes) | `40b1713a447c482950785428e746b696bce5fcf4d8fd630ccd4c6fa6a7fabc48` |
| [0235](https://bigsoundbank.com/s0235.html) | Iron bar falls #2 | https://bigsoundbank.com/UPLOAD/bwf-en/0235.wav (101,052 bytes) | `9ce86b2c5bae13b478ecbb8e02c07d9ef00db5d48eebd4ebb09b9338b10a7632` |
| [0297](https://bigsoundbank.com/s0297.html) | Adhesive tape #1 | https://bigsoundbank.com/UPLOAD/bwf-en/0297.wav (1,537,008 bytes) | `1f025c416d3eb2d856b855bd0f18d491532ac14e99ae37902c0ef7ee4f4850c2` |
| [0298](https://bigsoundbank.com/s0298.html) | Adhesive tape #2 | https://bigsoundbank.com/UPLOAD/bwf-en/0298.wav (705,904 bytes) | `352f3004650bf0a7aa31ab1c5ffe0cf25c4af6434070a9f68b219da406695d32` |
| [0299](https://bigsoundbank.com/s0299.html) | Adhesive tape #3 | https://bigsoundbank.com/UPLOAD/bwf-en/0299.wav (535,536 bytes) | `84e5c140ffe095dadfa49190ffc0346ce3b20e74c365163c80c40b7c236eff31` |
| [0302](https://bigsoundbank.com/s0302.html) | Creaking Door #1 | https://bigsoundbank.com/UPLOAD/bwf-en/0302.wav (219,890 bytes) | `5517b877c776f2749d91d43b3703d42b0de8c56df11d2c71b1af2c8550db98e3` |
| [0495](https://bigsoundbank.com/s0495.html) | Steps in the Mud | https://bigsoundbank.com/UPLOAD/bwf-en/0495.wav (918,852 bytes) | `1fc9a1686e1441cb17c317943208aba4fa2e084f6281b9759d2da64e6fe6de25` |
| [0513](https://bigsoundbank.com/s0513.html) | Sliding automatic gate | https://bigsoundbank.com/UPLOAD/bwf-en/0513.wav (2,769,104 bytes) | `f6d205ecaebdb75298770feedae0199ba17b7a447c0d025e6a8f1cf834298463` |
| [0516](https://bigsoundbank.com/s0516.html) | Wooden chair falling on carpet | https://bigsoundbank.com/UPLOAD/bwf-en/0516.wav (2,634,538 bytes) | `8e6ab6dd50d04e61efe82e21e54c6946eb0da4d1ea0baa22b2926c799677a89b` |
| [0536](https://bigsoundbank.com/s0536.html) | Ax on Wood | https://bigsoundbank.com/UPLOAD/bwf-en/0536.wav (1,872,916 bytes) | `c5af139a5f47940df3e74093d967df7861d71aa0b77474b5e62336bb8f298cbb` |
| [0548](https://bigsoundbank.com/s0548.html) | An arrow on a target | https://bigsoundbank.com/UPLOAD/bwf-en/0548.wav (466,258 bytes) | `fe7c1596c4c5763a75f7b3faa6cf8f7b2b28cfa65af7ed41e6ea7d503436d865` |
| [0549](https://bigsoundbank.com/s0549.html) | Archery | https://bigsoundbank.com/UPLOAD/bwf-en/0549.wav (88,464 bytes) | `ff529bedf49025d21e48f06007e67a0cfc46acc4345abff32c7adad3806ca126` |
| [0554](https://bigsoundbank.com/s0554.html) | Rope | https://bigsoundbank.com/UPLOAD/bwf-en/0554.wav (2,836,328 bytes) | `894291f9a17fd9cf9c4b598d9da088a754a9237beec54a95f14f68bd795b70e9` |
| [0601](https://bigsoundbank.com/s0601.html) | Trade grid: Closed | https://bigsoundbank.com/UPLOAD/bwf-en/0601.wav (1,731,892 bytes) | `1ec7ec03bca7071833e26d981d24612d421729f77a081076a010ed3225bcdcd6` |
| [0616](https://bigsoundbank.com/s0616.html) | Creaking metallic door | https://bigsoundbank.com/UPLOAD/bwf-en/0616.wav (7,897,468 bytes) | `7095712d8a36ad71890eeac768ddc6dabcfd8894bef70dba20d430e8745c2bdd` |
| [0683](https://bigsoundbank.com/s0683.html) | Grinding metal gate | https://bigsoundbank.com/UPLOAD/bwf-en/0683.wav (2,346,512 bytes) | `e1228a560ddd15f886d18a1e49b6849d85b698aa4a9c37507fb4fa765e11a12d` |
| [0690](https://bigsoundbank.com/s0690.html) | Shoes in the Water | https://bigsoundbank.com/UPLOAD/bwf-en/0690.wav (5,036,654 bytes) | `69b9672d3d4a62d930c404383c41112310be461b4ec0ee58fc1550a79ac2239b` |
| [0698](https://bigsoundbank.com/s0698.html) | Sea Waves | https://bigsoundbank.com/UPLOAD/bwf-en/0698.wav (32,020,114 bytes) | `bf3d671f1a35bfede601c2c089b4d8e4934c24ef71a63bebc64d93fca1256d91` |
| [0705](https://bigsoundbank.com/s0705.html) | Ax on log | https://bigsoundbank.com/UPLOAD/bwf-en/0705.wav (2,285,608 bytes) | `e726087850da66823849af599dc951b4b7b433bb76e46579c55be47682fb15f9` |
| [0758](https://bigsoundbank.com/s0758.html) | Beeps of digital code | https://bigsoundbank.com/UPLOAD/bwf-en/0758.wav (139,720 bytes) | `9dbeef051aff82f1dff49540f64f92f223b61b8238ef0ec129f2d5a447f787b5` |
| [0854](https://bigsoundbank.com/s0854.html) | Steps in the short grass | https://bigsoundbank.com/UPLOAD/bwf-en/0854.wav (2,894,298 bytes) | `cc34d25dda9c0cba1cb881bba0ab837e3cdb12d0aef1fcbeeb51705af7d0353d` |
| [0898](https://bigsoundbank.com/s0898.html) | Countryside #2 | https://bigsoundbank.com/UPLOAD/bwf-en/0898.wav (13,900,056 bytes) | `25f40521e5aef25a33711cb05a43cd9e2a5e36dd8d978097125efa36f5d19b6c` |
| [0904](https://bigsoundbank.com/s0904.html) | Wind in the Trees | https://bigsoundbank.com/UPLOAD/bwf-en/0904.wav (41,941,854 bytes) | `0977950ade4efe3c1a975c4c7c983b1487bfeb4adb456895e4d0b637ba81373b` |
| [0956](https://bigsoundbank.com/s0956.html) | Crows #2 | https://bigsoundbank.com/UPLOAD/bwf-en/0956.wav (413,100 bytes) | `aa19a66caba75a223b5458840b0a9b6c4533fd76a84e8f12a3cd475d629b5e8d` |
| [1022](https://bigsoundbank.com/s1022.html) | Fall of Stone | https://bigsoundbank.com/UPLOAD/bwf-en/1022.wav (2,814,072 bytes) | `f27b9cc4d8bfd2417eb474a508ae8623ddc6cb0e43f03badecf738d7bfeb3203` |
| [1023](https://bigsoundbank.com/s1023.html) | Explosion far Away #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1023.wav (436,220 bytes) | `9b77f30f6a27b2e7a32dce4a39ed14d4c19933341bcbcc01350c225a90a5f700` |
| [1113](https://bigsoundbank.com/s1113.html) | Apple crunched #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1113.wav (1,296,964 bytes) | `e61cca2148e9dc8308c5eebc37e445eec1677f4ae6773ed0c385c35d4ff630c6` |
| [1114](https://bigsoundbank.com/s1114.html) | Apple crunched #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1114.wav (765,836 bytes) | `401a107daac65c09b32d549e8416af719a23fbdfd8329af2c6bef880ffe4c698` |
| [1127](https://bigsoundbank.com/s1127.html) | Log wood boiler #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1127.wav (21,034,624 bytes) | `b345e0ae09b34e30094aa3db20c0f832144699ea5ea8d8ec2e4cedfb1013a627` |
| [1253](https://bigsoundbank.com/s1253.html) | Steps in the Grass, Slow | https://bigsoundbank.com/UPLOAD/bwf-en/1253.wav (3,370,818 bytes) | `d09ce9a99cfc36046392f411df230544ffcb9a5733bc9a7749915f67aa7e2570` |
| [1271](https://bigsoundbank.com/s1271.html) | Match #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1271.wav (1,512,262 bytes) | `f778c6e53449f04fcebcc4f9606291df7b7108841b641ad5707b537067d9af75` |
| [1299](https://bigsoundbank.com/s1299.html) | Broken twigs #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1299.wav (623,190 bytes) | `642173b3ee69b4245ee592291edbbaa674fffb7bf092fecef6663750d0024970` |
| [1306](https://bigsoundbank.com/s1306.html) | Slow wick 1 | https://bigsoundbank.com/UPLOAD/bwf-en/1306.wav (1,532,360 bytes) | `d94c18ca584c546ef1bd8a41f51da59558c8f15b2f8e0cea2362924d5b5c02f9` |
| [1318](https://bigsoundbank.com/s1318.html) | Fast Steps on Concrete | https://bigsoundbank.com/UPLOAD/bwf-en/1318.wav (8,863,310 bytes) | `2bf4901cf4e2e1352f34246fbaaed1bdec8b8362ae9dd36022abbeea35267b55` |
| [1340](https://bigsoundbank.com/s1340.html) | Gasoline fire #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1340.wav (1,336,244 bytes) | `9c95cd88ef7a2f02183edfeb22c4b833bc95d8fe1e4b467bf6f663f867f132be` |
| [1341](https://bigsoundbank.com/s1341.html) | Gasoline Fire #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1341.wav (4,132,026 bytes) | `828c16943d1bd1c2990c1cb56867b501ef650fda281f67d9da086b502e0a788f` |
| [1342](https://bigsoundbank.com/s1342.html) | Gasoline fire #3 | https://bigsoundbank.com/UPLOAD/bwf-en/1342.wav (14,727,240 bytes) | `bcef98d7a9f5f8baa0b10fc25adf7d72c0cde3001934333ee09b980b53d96758` |
| [1355](https://bigsoundbank.com/s1355.html) | Cartridge case 9 mm on concrete #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1355.wav (133,444 bytes) | `f6e057a50a6a1c6bc81107bfc449be7f2072fe278d4b9d8a899e5b461d5d9acc` |
| [1356](https://bigsoundbank.com/s1356.html) | Cartridge case 9 mm on concrete #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1356.wav (128,962 bytes) | `8bf6f53e79ca157a82d4d5dd442a13cab4ca1625c9a9a50e71919f91ac15f5fe` |
| [1361](https://bigsoundbank.com/s1361.html) | Cartridge case 5.56mm on concrete #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1361.wav (144,104 bytes) | `428c7ab473228139dc16968f8417ef941701580582767676add22e04864f8c06` |
| [1367](https://bigsoundbank.com/s1367.html) | Cartridge case 7.62mm on concrete #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1367.wav (145,004 bytes) | `32b91f41785d61b68a5174a231314d8912c7065f263bd13ff30260dbd7f770df` |
| [1514](https://bigsoundbank.com/s1514.html) | Rowing slowly | https://bigsoundbank.com/UPLOAD/bwf-en/1514.wav (9,550,640 bytes) | `4bdf6d1e97ed5e8dc0e5baf7e401dd43de8521ab2f42c18ae0112872ee592cea` |
| [1516](https://bigsoundbank.com/s1516.html) | Steps on a wooden floor #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1516.wav (3,766,446 bytes) | `ec9d7af3dd06f4353ff1ea9c9277acb8ffa77f5ce8342ae13921f95ae677e18a` |
| [1519](https://bigsoundbank.com/s1519.html) | Splash, Big #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1519.wav (471,616 bytes) | `7b95e7ad63da72be37ac9879d932512f55bc129632ed75711277d9564fe17212` |
| [1520](https://bigsoundbank.com/s1520.html) | Splash, big #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1520.wav (1,068,008 bytes) | `f10c8bf572f0f537fd1dfda5946e4fa06d321ab0e62f50bedfb53b30f18d0186` |
| [1521](https://bigsoundbank.com/s1521.html) | Splash, Big #3 | https://bigsoundbank.com/UPLOAD/bwf-en/1521.wav (590,896 bytes) | `9dcb2e792ce2cebead8202c3b2451f7f054a4ca3e80c944e6d831e79d984f32d` |
| [1529](https://bigsoundbank.com/s1529.html) | Splash, Small #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1529.wav (378,626 bytes) | `fd7504403d6c176b598c0023cb7f6e5dcbbf6176b5ad9742fece4ef07237244f` |
| [1530](https://bigsoundbank.com/s1530.html) | Splash, small #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1530.wav (341,978 bytes) | `bb9e400265237ae909fbad1623433423cb5cef647e7f04d381361f6cb65fb773` |
| [1531](https://bigsoundbank.com/s1531.html) | Splash, small #3 | https://bigsoundbank.com/UPLOAD/bwf-en/1531.wav (297,998 bytes) | `ac3ccb7e910a71e7298f29eeb33166166b1e83ce1ba61861b2d07a1fcb4acc04` |
| [1532](https://bigsoundbank.com/s1532.html) | Splash, small #4 | https://bigsoundbank.com/UPLOAD/bwf-en/1532.wav (338,312 bytes) | `1361474a52a9907247a93c233fe3ff3d03ba52950edf6dcda2773f0238cefaea` |
| [1533](https://bigsoundbank.com/s1533.html) | Splash, small #5 | https://bigsoundbank.com/UPLOAD/bwf-en/1533.wav (306,038 bytes) | `81a973ee9bc380fb16b325e7837a29a1cac628955be0970103bb49073a8bb204` |
| [1534](https://bigsoundbank.com/s1534.html) | Splash, small #6 | https://bigsoundbank.com/UPLOAD/bwf-en/1534.wav (181,508 bytes) | `abc547867935109ae7e91e6d0b43f27b2427e749e4b90932532f1f57b8959c82` |
| [1594](https://bigsoundbank.com/s1594.html) | Raw carrot crunched #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1594.wav (1,775,638 bytes) | `465873d310911abebfff3d36f5fd4b1ca53bc022369b5c682c2c540e40d39778` |
| [1694](https://bigsoundbank.com/s1694.html) | Quenching, cooling #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1694.wav (186,660 bytes) | `9547087b7c29883df4a34a6a42b83cfcfdded80cbec0b508dc3444c29499af50` |
| [1695](https://bigsoundbank.com/s1695.html) | Quenching, cooling #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1695.wav (309,466 bytes) | `5b13c9e466c941b8859fcfc2f77c707f32f0fe94960d5fabe9e84473189ed93a` |
| [1696](https://bigsoundbank.com/s1696.html) | Quenching, cooling #3 | https://bigsoundbank.com/UPLOAD/bwf-en/1696.wav (176,568 bytes) | `7d2aa51af532ea14b8ca032090c6064581173362b8682983ff93ec09f0edb21d` |
| [1704](https://bigsoundbank.com/s1704.html) | Apartment Door, Closing #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1704.wav (1,105,868 bytes) | `61aeb872af64e624148dcbfa95189db46762c516a05020bd93b5cbcaee8fb41c` |
| [1763](https://bigsoundbank.com/s1763.html) | Tawny Owl #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1763.wav (1,225,308 bytes) | `33235068932d6b1e65d2c5702fa703e983018229ec89263ebb57490e97bc2f1a` |
| [1764](https://bigsoundbank.com/s1764.html) | Tawny Owl #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1764.wav (1,141,190 bytes) | `3fec9ca640d658b7016889f6d7964fe3d1623fef97296b30a367bad483b1f6ab` |
| [1771](https://bigsoundbank.com/s1771.html) | Falling sheet steel bar #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1771.wav (148,860 bytes) | `780f68676cb6e23f282c1433bc5e895b6fe009d0197c328748f0754510918d12` |
| [1772](https://bigsoundbank.com/s1772.html) | Falling sheet steel bar #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1772.wav (230,174 bytes) | `4325626df2b9518fc229c8844d65737a96631e6e68f9de3088a651c9b88e0af2` |
| [1773](https://bigsoundbank.com/s1773.html) | Falling sheet steel bar #3 | https://bigsoundbank.com/UPLOAD/bwf-en/1773.wav (229,166 bytes) | `43931bf9e038e30a6d00d404660a44fba6d171fc39faefaf51578453f6640f00` |
| [1774](https://bigsoundbank.com/s1774.html) | Falling sheet steel bar #4 | https://bigsoundbank.com/UPLOAD/bwf-en/1774.wav (555,810 bytes) | `c928012d238efd436047cc7291e063a2b8d7a56d221ec0f1dd36fb2899d4f7f8` |
| [1775](https://bigsoundbank.com/s1775.html) | Falling sheet steel bar #5 | https://bigsoundbank.com/UPLOAD/bwf-en/1775.wav (302,516 bytes) | `98eed9d2636b62518aed2034a0ac0bfcb9c186c997123173a9cfee4bd29d5bd1` |
| [1776](https://bigsoundbank.com/s1776.html) | Fall 2 sheet steel bars #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1776.wav (492,482 bytes) | `30cc71b0d17a362fb4aa52d45ab325c350d17b27f4fad48babbc43d138dc5f10` |
| [1777](https://bigsoundbank.com/s1777.html) | Fall 2 sheet steel bars #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1777.wav (311,630 bytes) | `6fbac114556dc26258f2b87204fc62d17b204b2e3b35b174de7421a946a640fc` |
| [1778](https://bigsoundbank.com/s1778.html) | Fall 2 sheet steel bars #3 | https://bigsoundbank.com/UPLOAD/bwf-en/1778.wav (631,172 bytes) | `cd02e5abbca7d756fbfc30a0604ed089a03fe19bc5db7e5ddcb20ca11790992a` |
| [1795](https://bigsoundbank.com/s1795.html) | Whoosh #3 | https://bigsoundbank.com/UPLOAD/bwf-en/1795.wav (264,458 bytes) | `09117c6e7d1f5c9a6fc30c0be573ab715fb5c65e006650a3f9a19f4d8b7372ef` |
| [1796](https://bigsoundbank.com/s1796.html) | Whoosh #4 | https://bigsoundbank.com/UPLOAD/bwf-en/1796.wav (299,762 bytes) | `e0f841b5f734daccbab4def7103872399c9ab59e21efda705e5465047229e6b5` |
| [1797](https://bigsoundbank.com/s1797.html) | Whoosh #5 | https://bigsoundbank.com/UPLOAD/bwf-en/1797.wav (155,780 bytes) | `c0e71fd98dcde43e674092f967b32458b6fd827ec32d78395c9f498538dbc5c7` |
| [1798](https://bigsoundbank.com/s1798.html) | Whoosh #10 | https://bigsoundbank.com/UPLOAD/bwf-en/1798.wav (133,974 bytes) | `4fe8240ba612cd613f04a646169198bb3c1458238237d4f4398a47e97c3c9bcb` |
| [1799](https://bigsoundbank.com/s1799.html) | Whoosh #6 | https://bigsoundbank.com/UPLOAD/bwf-en/1799.wav (150,164 bytes) | `0ccc87ea2ee12102e1c8fae9e9282433030c4b15a81e97241cd112203d99ea93` |
| [1800](https://bigsoundbank.com/s1800.html) | Whoosh #7 | https://bigsoundbank.com/UPLOAD/bwf-en/1800.wav (256,730 bytes) | `971a524690a44b5bdf4dd8c75807ea8d692a6d1475055bb84c08aa96568c7242` |
| [1806](https://bigsoundbank.com/s1806.html) | Explosion far away #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1806.wav (100,766 bytes) | `2c01b9edaa220e0332ae7bdfdb1aa0b064e4636357e3575df950176474a0cbbb` |
| [1807](https://bigsoundbank.com/s1807.html) | Explosion #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1807.wav (31,516 bytes) | `d9e569e5d75bfdd1b48a0d15e81cd9cd63855e356a8964b34ec44caa5ed98433` |
| [1808](https://bigsoundbank.com/s1808.html) | Explosion #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1808.wav (47,992 bytes) | `d0c74f7c01ab4bebc45f346696be1167af9b6e6c932528215aa0df9932a639bb` |
| [1841](https://bigsoundbank.com/s1841.html) | Jumping on wooden pontoon #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1841.wav (158,970 bytes) | `c2d31a189daa0f79c4d2dec55b612849c2074bd445d026d529093282598b2cd2` |
| [1842](https://bigsoundbank.com/s1842.html) | Jumping on wooden pontoon #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1842.wav (170,978 bytes) | `f0c80731f0b68a0e99fcf01815f847b186d09c6b6fc18cc63c98611e20a18be5` |
| [1843](https://bigsoundbank.com/s1843.html) | Running in the Tall Grass | https://bigsoundbank.com/UPLOAD/bwf-en/1843.wav (2,862,968 bytes) | `8e37390e7ab120413ed468d21f6b37d4ade35540ceab7ad88962e5dcf2803c6d` |
| [1880](https://bigsoundbank.com/s1880.html) | Campaign at Night #4 | https://bigsoundbank.com/UPLOAD/bwf-en/1880.wav (86,142,608 bytes) | `fadf592cbf454e854c708673d0abcb916413a020b08598cd1860a62985e5add1` |
| [1984](https://bigsoundbank.com/s1984.html) | Pistol, cocking #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1984.wav (274,934 bytes) | `e017942eaeb58280dec5e0d55e6d8cb7ec150ffe658b866a6af002905d98549c` |
| [1985](https://bigsoundbank.com/s1985.html) | Pistol, cocking #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1985.wav (69,824 bytes) | `36bd10b656553a253801a25ce63b4135564b422fce2a834e1eb00398fe4eec05` |
| [1986](https://bigsoundbank.com/s1986.html) | Pistol, cocking #3 | https://bigsoundbank.com/UPLOAD/bwf-en/1986.wav (66,566 bytes) | `fb5a05f2cb04d43794ae42b632ccfed23105221eaad54384537fead41f4f8220` |
| [1987](https://bigsoundbank.com/s1987.html) | Pistol, Cocking #4 | https://bigsoundbank.com/UPLOAD/bwf-en/1987.wav (66,566 bytes) | `21e2dc1674762b8a3b9f28006d1686b8460732084608a47275a42e10e91ad62d` |
| [1988](https://bigsoundbank.com/s1988.html) | Gun, loading #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1988.wav (112,320 bytes) | `4f752adea78fc7a3aa6c06a13dbd563eadb7c0a5e4d083748306f79b8a963265` |
| [1990](https://bigsoundbank.com/s1990.html) | Gun, loading #3 | https://bigsoundbank.com/UPLOAD/bwf-en/1990.wav (122,120 bytes) | `f57dd15cce786688bd9705fe71c5330a3804c2b647e727467fa1d4d72629bc7c` |
| [1991](https://bigsoundbank.com/s1991.html) | Gun, loading #4 | https://bigsoundbank.com/UPLOAD/bwf-en/1991.wav (131,928 bytes) | `6041ad0cc69b61c214648964706c7dc0805b14bbb6a974be85d8a94712906e18` |
| [1994](https://bigsoundbank.com/s1994.html) | Pistol, disarming hammer #1 | https://bigsoundbank.com/UPLOAD/bwf-en/1994.wav (40,418 bytes) | `35127006f22b12be93eca1de7cf5776e13fe21b3fb328d88ded4c04d99e8f7f1` |
| [1995](https://bigsoundbank.com/s1995.html) | Pistol, disarming hammer #2 | https://bigsoundbank.com/UPLOAD/bwf-en/1995.wav (63,326 bytes) | `b942568e1f2b26e3d713cfcd385f34104832e1aed3dec61f2be950fb538f0d63` |
| [2195](https://bigsoundbank.com/s2195.html) | Man Breathing | https://bigsoundbank.com/UPLOAD/bwf-en/2195.wav (11,041,260 bytes) | `d077b2743eeeaa69f5f2f993ee16b4441d1cd32211774c22bc885c29150d2bb6` |
| [2431](https://bigsoundbank.com/s2431.html) | Door, closing #1 | https://bigsoundbank.com/UPLOAD/bwf-en/2431.wav (267,402 bytes) | `b10e25e3efb8d4f0da8955a22e2bbee0e6507ae59fcf461227b997bea2c70c7a` |
| [2432](https://bigsoundbank.com/s2432.html) | Door, closing #2 | https://bigsoundbank.com/UPLOAD/bwf-en/2432.wav (293,446 bytes) | `72678d2a0359f4256164442fd2093a03099ff0093aef350e5f6000171a0ddc23` |
| [2452](https://bigsoundbank.com/s2452.html) | Body Fall #1 | https://bigsoundbank.com/UPLOAD/bwf-en/2452.wav (222,032 bytes) | `4770f7b32fcab7437c5b1ce79b80df592f3ff76885c49c62fb367d74a4f3430c` |
| [2453](https://bigsoundbank.com/s2453.html) | Body Fall #2 | https://bigsoundbank.com/UPLOAD/bwf-en/2453.wav (221,794 bytes) | `852d68dbf84df1399182dbb938594203a80eea24a681ac4ffe473dc0dca3b3ad` |
| [2454](https://bigsoundbank.com/s2454.html) | Body fall #3 | https://bigsoundbank.com/UPLOAD/bwf-en/2454.wav (213,650 bytes) | `5fb4940962fcfd5f00c516c204f46036ae8046ddb201a2913e1eac557d8c2a08` |
| [2459](https://bigsoundbank.com/s2459.html) | Punch #4 | https://bigsoundbank.com/UPLOAD/bwf-en/2459.wav (123,806 bytes) | `140e2b97f610d3b5035d17b70fbdd39e207da7dc289dda89417d4c13eff2f851` |
| [2460](https://bigsoundbank.com/s2460.html) | Punch #5 | https://bigsoundbank.com/UPLOAD/bwf-en/2460.wav (123,770 bytes) | `2c40a361dbaa2f01971448215df2e5ff2925d4b7ccfd8d390e5ed073610e3db3` |
| [2461](https://bigsoundbank.com/s2461.html) | Punch #6 | https://bigsoundbank.com/UPLOAD/bwf-en/2461.wav (128,938 bytes) | `3ce4c0be524362ec90d6a5591dba940ed88c3b5e5f392f0783031246f48389dd` |
| [2462](https://bigsoundbank.com/s2462.html) | Punch #7 | https://bigsoundbank.com/UPLOAD/bwf-en/2462.wav (98,510 bytes) | `37d4ffe3a9c953b6eb5a8318b5783f3a40a149117be181efb7a2a79c85659c53` |
| [2463](https://bigsoundbank.com/s2463.html) | Punch #8 | https://bigsoundbank.com/UPLOAD/bwf-en/2463.wav (137,518 bytes) | `338c14568c0e8e49a34f20c826d2260bce38c67b1bfbe74a4b73c68f07933c90` |
| [2464](https://bigsoundbank.com/s2464.html) | Punch #9 | https://bigsoundbank.com/UPLOAD/bwf-en/2464.wav (106,856 bytes) | `519e58c2f719fd80bd67b85b50fcbed88ce710e8248c44dca2515ad3c6eed85d` |
| [2715](https://bigsoundbank.com/s2715.html) | Forest #3 | https://bigsoundbank.com/UPLOAD/bwf-en/2715.wav (46,256,014 bytes) | `79ca81aa11e27173ab83df44a53a0969a99f3c6a4d54a461f5a82e81cfd6317c` |
| [2718](https://bigsoundbank.com/s2718.html) | Thunder #1 | https://bigsoundbank.com/UPLOAD/bwf-en/2718.wav (3,306,598 bytes) | `09269be64e0a13da0361e20c1ad14612b995d4211b8cd346b698e6d277bc8168` |
| [2857](https://bigsoundbank.com/s2857.html) | Fireplace #5 | https://bigsoundbank.com/UPLOAD/bwf-en/2857.wav (13,047,100 bytes) | `df562c3e0674e4d606f257e79931bd1e21c0e9eac20dfef24208acc16b561240` |
| [2889](https://bigsoundbank.com/s2889.html) | Feet in leaves #2 | https://bigsoundbank.com/UPLOAD/bwf-en/2889.wav (9,427,280 bytes) | `47fc7e17bf2745be891b306d7adbb6b6f8e2892be2b3153a48cde22aa493ffeb` |
| [3081](https://bigsoundbank.com/s3081.html) | Apple crunched #3 | https://bigsoundbank.com/UPLOAD/bwf-en/3081.wav (3,524,340 bytes) | `f33601bcadd7616eb94a7095db0a765fd68115434d627d8158a572e943ccbddb` |
| [3113](https://bigsoundbank.com/s3113.html) | Thunder #2 | https://bigsoundbank.com/UPLOAD/bwf-en/3113.wav (725,418 bytes) | `88b9cc1944d63662da3989114683ac616b0e93480d8b5fe4547fca7f52de17c2` |
| [3114](https://bigsoundbank.com/s3114.html) | Thunder #3 | https://bigsoundbank.com/UPLOAD/bwf-en/3114.wav (1,614,196 bytes) | `448d45edf4022a0ac979a076e997321abcef3807cc8f41dcae2be2e317360255` |
| [3179](https://bigsoundbank.com/s3179.html) | Thunder #6 | https://bigsoundbank.com/UPLOAD/bwf-en/3179.wav (1,982,322 bytes) | `1fdd78bec376bd36c13ad2463f05356ce83fdcf4d1fecbc18d421fcfa5aa51f2` |
| [3205](https://bigsoundbank.com/s3205.html) | Creaking Door #2 | https://bigsoundbank.com/UPLOAD/bwf-en/3205.wav (456,256 bytes) | `864dd8c1b216639d334847ceabcd16264e1a6ecacf6cc2353a14d458878eda56` |
| [3211](https://bigsoundbank.com/s3211.html) | Creaking Door #8 | https://bigsoundbank.com/UPLOAD/bwf-en/3211.wav (355,034 bytes) | `fa56af8d79f381cff34312e9ef8617384478c258dd7937c5cef6fd67a87049b8` |
| [3216](https://bigsoundbank.com/s3216.html) | Footsteps on Gravels #4 | https://bigsoundbank.com/UPLOAD/bwf-en/3216.wav (6,802,382 bytes) | `8e7f5bdecf9729c32e040a08027dcb9735d3eab34b49535bcd2852d8bd6daf18` |
| [3247](https://bigsoundbank.com/s3247.html) | Drink from the gourd #1 | https://bigsoundbank.com/UPLOAD/bwf-en/3247.wav (1,627,928 bytes) | `9915e6d9192ae95714f2158453a9df0f8435e46d91613eba95e906210d3f3645` |
| [3248](https://bigsoundbank.com/s3248.html) | Drink from the gourd #2 | https://bigsoundbank.com/UPLOAD/bwf-en/3248.wav (2,342,432 bytes) | `22ed35797fb1551a58a8fd2b4e1fe39a78aacebce2470e0d105c7b56de9a2188` |
| [3249](https://bigsoundbank.com/s3249.html) | Drink from the gourd #3 | https://bigsoundbank.com/UPLOAD/bwf-en/3249.wav (1,831,962 bytes) | `8084dff1c21f5fdbf248f675fc897a067bf8fee09f5bbeca042d14ed17117326` |
| [3363](https://bigsoundbank.com/s3363.html) | Steps in the snow #3 | https://bigsoundbank.com/UPLOAD/bwf-en/3363.wav (4,380,074 bytes) | `4e1ac25a697a1ea7ef913ab21fba280683ca36a82156c62576551819b7896275` |
| [3463](https://bigsoundbank.com/s3463.html) | Carrion Crow #3 | https://bigsoundbank.com/UPLOAD/bwf-en/3463.wav (392,316 bytes) | `46bc06fea635eefeafe35751c175dea30b6cd47c03695bbf24b3ecd81cf8ea42` |
| [3589](https://bigsoundbank.com/s3589.html) | Anvil #1 | https://bigsoundbank.com/UPLOAD/bwf-en/3589.wav (4,727,472 bytes) | `ebc51af2da517a3b3a2e21a7f7be1ce0c5942f3583a0a8702506af87d4ca10c0` |
| [3590](https://bigsoundbank.com/s3590.html) | Anvil #2 | https://bigsoundbank.com/UPLOAD/bwf-en/3590.wav (3,170,872 bytes) | `62c4aa8d870f3a5a7b94825bd5cbf44261013f430169b589536a4402520367d1` |
