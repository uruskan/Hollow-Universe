# Hollow Salvage R01

Six original ground pickups for `gears`, `springs`, `metal-pipe`, `sheet-metal`, `tech-scrap` and `rope`. **Ready for review**, with editable source, image-free GLBs, textured Godot scenes, collision, three LODs and transparent 128/256 icons. The sheet-metal revision follows the owner's correction: two thin corrugated offcuts with bent corners. The 15 October material pass replaces repetitive ridge bands with broad dull zinc, isolated irregular dark corrosion, sparse runoff and selected contact wear. Gears, pipe and electronics now carry localized use and dirt within the existing atlas. See `Reports/Revisions/2026-10-15_material/CHANGELOG.md` and `Renders/material_revision_before_after.png`. No game catalogue or gameplay files were edited.

Open **View.cmd** for the standalone Godot 4.7.2 .NET inspection scene. It uses the shared heavy-job wrapper and will wait if another production session owns the slot. Close the preview when finished so the slot is released.

| Control | Action |
|---|---|
| Right mouse + WASD | Fly and look |
| Q / E, Shift | Down / up, faster |
| 1 / 2 | Overview / 1.7 m player eye |
| L | Automatic → LOD0 → LOD1 → LOD2 |
| H / Escape | Hide overlay / release pointer |

## Assets and budgets

| Key | LOD0 / LOD1 / LOD2 triangles | Material surfaces per LOD |
|---|---:|---:|
| gears | 2,304 / 1,036 / 344 | 1 |
| springs | 2,700 / 1,214 / 404 | 1 |
| metal-pipe | 1,536 / 690 / 230 | 1 |
| sheet-metal | 2,272 / 1,022 / 340 | 1 |
| tech-scrap | 2,016 / 906 / 296 | 1 |
| rope | 4,392 / 1,976 / 658 | 1 |

Collision is excluded. Each pickup has one closed 12-triangle convex envelope; it fills gear/pipe/rope holes and empty assembly space. This is a pickup approximation, not an exact per-part collision surface. Rope's higher budget preserves the winding and tail silhouette; fibre detail comes from normal maps. Electronics LOD2 is deliberately rebuilt without small solder legs and vent detail. LOD1 is ~45%, LOD2 ~15%. At enlarged inspection scale the distant LODs visibly simplify teeth, coils and corners; the native switch distances are 6 and 16 m, with culling at 45 m.

Metres, Blender Z-up → glTF/Godot Y-up. Every exported mesh is in asset-local coordinates, with a bottom-centre footprint pivot and no runtime scale override. The modest 0.25–0.32 m spans preserve handheld pickup scale; exact measured bounds are in HANDOFF.json. Source collections are spaced for editing only. These resources have no held animation, sockets or moving gameplay parts.

## Appearance and UVs

Use `Scenes/<key>.tscn` for the intended material setup. These scenes contain mesh data and resolve `Materials/` and `Textures/` inside this package. GLBs intentionally have no images, matching the receiving game's tests. Their adjacent `.import` files map named materials to external resources. Keep package folders at the same resource root or remap `res://` paths when integrating.

Three 2048² runtime PBR sets: `SalvageTrim` shared by gears/spring/pipe/electronics, `CorrugatedSteel` for the revised sheet bundle, and `RopeFibre`. Each supplies BaseColor (sRGB), NormalGL (linear, OpenGL +Y), and ORM (linear R=AO, G=roughness, B=metallic). AO is neutral. Trim and rope have original native 2048² procedural sources. The galvanized sheet uses one built-in ImageGen diffuse material source, actually returned at **1254 × 1254**, resampled to the 2048² runtime set; this adds no source colour detail. Its exact prompt and unmodified PNG are in `Source/MaterialSources/`. Zinc/oxide colour-class masks drive separately authored roughness and metalness; only local high-pass luminance residual contributes restrained surface normals. No low-frequency image brightness is treated as geometry. The source was requested with flat diffuse lighting; it is a generated albedo approximation, not a measured scan or calibrated reflectance capture. No web or owner-reference pixels were incorporated. Godot import enables mipmaps and high-quality VRAM compression. Nine RGBA8 maps with full mip chains have a 192 MiB decoded upper bound; BC7/BC5 at 8 bits/pixel are approximately **48 MiB including mipmaps**, excluding diagnostics and engine overhead. The validated final import uses BC7 (BptcRgba) for all nine maps, totaling 50,331,888 bytes (48.00023 MiB) of compressed mip-chain image data. Reports distinguish this measured image storage from total driver allocation.

UV0 is intentionally shared/trimmed, not a unique bake layout. Rigid steel uses a 0.32 × 0.64 m atlas tile basis (~1,600 texels/m before trim margins); PCB graphics fill their dedicated tile. Tube sides have analytical circumferential and longitudinal coordinates. Steel tube strips repeat deliberately every 0.12 m inside the trim; the rope's continuous V advances by integrated centreline length divided by cord circumference, with a longitudinal seam and planar end caps. Successive rope turns are not projected onto each other. Sheet U follows integrated corrugation length and V runs along the sheet; two sheets intentionally share the material, with the second rotated 180 degrees in UV space to avoid a texture wrap seam. The pipe now uses a dedicated cylindrical region and a separate oily bore strip inside the same atlas; gear contact flanks use the existing brushed-steel tile. These allocations prioritize visible construction wear and do not claim identical density between all regions. No UV1 or unique bake-UV claim is made. Retained bevels and authored surface normals carry detail; there is no claimed high-to-low bake.

## Review evidence

Start with `Renders/inventory_silhouettes.png`, `material_closeups.png`, `neutral_views.png`, `player_distance.png`, `wireframe.png`, `uv_checker.png`, `lod_comparison.png` and `engine_family.png`. These are actual Godot output or labelled layouts of those captures, without paint-over. Individual raw frames remain alongside them. `engine_capture_bindings.json` binds every raw capture and icon to current model, scene, texture, material and preview-code SHA-256 hashes; final packaging rejects stale evidence. Icons follow the receiving pipeline: transparent 1024² render, [35,26,0] camera, 86% framing, alpha-weighted area reduction and alpha-edge repair.

`Reports/glb_validation.json` checks actual exported bytes for geometry/UV degeneracy, duplicate faces, welded topology, normals, tangents, convexity, materials, image-free export and budgets. `engine_library_validation.json` checks all six reusable scenes. `engine_lod_validation.json` compares automatic distance bands to forced LODs at 5, 7, 17 and 46 m, using exact alpha masks, matching draw counts and a bounded RGB difference. The test isolates geometry with light shadows disabled; normal review captures retain shadows. A small electronic-heatsink shading difference can occur between automatic and forced draws; it does not change the silhouette or draw count, and the measured difference is recorded. This is not a full-game performance claim.

`Reports/catalogue_mapping_proposal.json` is a review proposal only. It adds the missing `icon` metadata, removes stand-in notes in the proposal and identifies material/path remaps. Main-game integration, catalogue tests and world placement are the coordinator's next step.

## Rebuild and provenance

Run `Source/Rebuild.ps1` to rebuild textures, meshes, static audit, C# preview, imports, reusable scenes and captures through the shared wrapper. It overwrites generated files in this package; preserve hand edits first. It requires the installed Blender 5.2.1, Godot 4.7.2 .NET, .NET 10, and Python with Pillow/NumPy. Exact commands and tool receipts are in Reports. `Source/audit.py` is the primary standalone static check. `Source/finish.py` validates delivery completeness and regenerates the handoff and SHA-256 manifest after visual review.

Original authored meshes/materials/code are supplied for the Hollow project; no blanket public-domain license is asserted. The owner's reference image guided corrugated-sheet construction and material direction only: no pixels or extracted geometry are included. The binary GLB audit helper adapts the supplied Substation reference's local code; the reference package was read-only. `Reports/provenance.json` records source hashes. The optional `game-dev` CLI was not found on PATH or the inspected tool locations; this delivery uses the authorized Blender/Python workflow with explicit byte, engine and package verification, and does not claim a game-dev registry receipt or AAA approval.
