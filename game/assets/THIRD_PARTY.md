# Third-party files in the game

Every file here that we didn't make, with its source and licence (GROUND.md §9).

| Path | What | Source | Licence |
|---|---|---|---|
| `fonts/Inter/` | Inter, variable font: the maps' interface font | [rsms/inter](https://github.com/rsms/inter) | SIL OFL 1.1 (`fonts/Inter/OFL.txt`) |
| `fonts/Barlow/` | Barlow Medium and SemiBold, Barlow Condensed Medium and Bold: the ground interface's fonts, as in RustMVP | [jpt/barlow](https://github.com/jpt/barlow), via RustMVP `assets/SourceUI/fonts` | SIL OFL 1.1 (`fonts/Barlow/OFL.txt`) |

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
