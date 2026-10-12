# Third-party files in game/assets

Every file here that was not made for Hollow Universe, with where it came from and under what terms. Plants, rocks,
grass, foliage and bark textures are made in code (`game/scripts/Surface/Flora`, GROUND.md §4.6) and are not listed.

## Fonts

| File | Source | Licence |
|---|---|---|
| `fonts/Inter/InterVariable.ttf` | Inter, by Rasmus Andersson (rsms.me/inter) | SIL Open Font License 1.1 (`fonts/Inter/OFL.txt`) |

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
