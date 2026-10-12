# Third-party files in the game

Every file here that we didn't make, with its source and licence (GROUND.md §9).

| Path | What | Source | Licence |
|---|---|---|---|
| `fonts/Inter/` | Inter, variable font: the maps' interface font | [rsms/inter](https://github.com/rsms/inter) | SIL OFL 1.1 (`fonts/Inter/OFL.txt`) |
| `fonts/Barlow/` | Barlow Medium and SemiBold, Barlow Condensed Medium and Bold: the ground interface's fonts, as in RustMVP | [jpt/barlow](https://github.com/jpt/barlow), via RustMVP `assets/SourceUI/fonts` | SIL OFL 1.1 (`fonts/Barlow/OFL.txt`) |
| `building/` (GLB) | The building kit's 13 pieces in 5 grades and the door and window kit's doors, garage door, bars, glass and embrasures, converted by `art/import/`; 7 more pieces built by the same scripts with the kit's own generator | `D:\GameDev\Assets\PSG_BuildingKit_R01`, `PSG_DoorWindowKit_R01`: our own kits, made by Blender Python (GROUND.md §9) | Ours |
| `building/textures/` | The kits' surface atlases, shrunk to 2K: Poly Haven's weathered brown planks, brown bark 02, rock surface, rusty metal sheet and painted concrete, as the kits' `texture_manifest.json` and `source_texture_provenance.json` record | [Poly Haven](https://polyhaven.com) | CC0 1.0 |
