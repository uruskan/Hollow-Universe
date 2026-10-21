# Hollow Loot Containers R01

Assignment 04: three original container refinements, each with intact and controlled damaged visuals, editable components, three LODs, and textured Godot scenes. Status: **ready for review**, subject to the evidence and limitations in `HANDOFF.json`. The game project was read only. No catalogue, gameplay, recycler, workbench or fuel-barrel changes were made.

| Container | Catalogue key | Intact triangles LOD0 / 1 / 2 | Distinct materials | Surfaces per visible LOD |
|---|---|---:|---:|---:|
| Timber salvage crate | `loot-crate` | 9,774 / 4,398 / 1,466 | 2 | 4 |
| Reinforced equipment crate | `military-crate` | 8,204 / 3,690 / 1,228 | 1 | 2 |
| Industrial loot drum | `loot-barrel-1` | 7,392 / 3,344 / 1,145 | 1 | 2 |

The damaged crate is 10,034 / 4,514 / 1,504 triangles; damaged equipment crate is 8,528 / 3,836 / 1,276; damaged drum matches intact. Collision is excluded. The timber crate uses four draw surfaces because two materials span independently moving body and lid; material count remains two. No hidden duplicate LODs are drawn in the supplied scenes.

## Inspect

Run `View.cmd` with Godot 4.7.2 .NET and .NET 10 installed. It builds C#, imports the package and launches through the shared heavy-job wrapper. The current machine paths are in the launcher; `GODOT` may override its executable. The viewer uses real Forward+ rendering. It is an inspection camera, not the game's player controller.

- Hold RMB and use WASD/QE to fly; Shift accelerates. Escape releases the pointer.
- 1 / 2 / 3 select overview, 1.7 m player-height view and equipment detail.
- O opens/closes the lids. V switches intact/damaged. L cycles LOD0/1/2/automatic.
- C shows the actual UV checker. F shows wireframe. H hides the overlay.
- `View.cmd -- --camera wood` or `--camera drum` selects individual detail views. `-- --suite` writes the full bounded capture suite and quits.

Final images are `Renders/engine-*.png`: front/back/side, player height, material details, damaged states, hinge opening, wireframe, checker and LOD comparisons. `Reports/review_contact.png` collects selected images. `Reports/visual_review.json` records the inspection and its limits.

## Reuse and source

`Source/Hollow_LootContainers_R01.blend` retains individual boards, straps, corner protectors, hinges, latches, lid pieces, drum shells, bungs and damage components. There are six named source collections arranged for inspection. Texture paths in the blend are relative. `Source/build.py` rebuilds and exports the family deterministically from local final maps.

`Models/*.glb` preserves root, `socket_use`, `Lid`, and separate `Body_lod0/1/2` and `Lid_lod0/1/2` meshes. Runtime copies are triangulated deliberately. GLBs follow the receiving pipeline's image-free material convention; standalone GLB import is not the complete appearance. Use the **nine ready-to-instance `.tscn` scenes** under `Scenes/`, including three open inspection states, alongside `Materials/` and `Textures/`. Their mesh resources and material references resolve within this package, without preview C# gameplay dependencies. When moving to a subfolder in another project, remap resource paths together.

Units are metres. Blender is Z-up, front −Y; glTF/Godot is Y-up, front +Z. Coordinate conversion is `[x,y,z] → [x,z,-y]`. Ground roots are stable, with all final intact minima at ground level. Use points retain the original source positions. Crate lids rotate −105° about local X at their rear-edge pivots. The drum has a clamp and a **lift-off** lid translated 0.28 m upward for inspection; no invented hinge is implied. Hinge pins/knuckles remain editable in source.

Each saved scene selects exclusive native LOD visibility bands at 12 and 28 m with shared bounds. The viewer can force a level for comparison. These are proposed inspection thresholds and need in-game tuning. Geometry at LOD1/2 is approximately 45%/15% of LOD0. Damage recipes retain the silhouette: one split timber plank; a scored steel panel, compressed lid corner and bent latch; a localized drum dent with matching inner wall.

## Materials and UVs

All runtime PBR maps are **native 2048×2048**. Shared `LC_Metal` is an original 16-cell painted-metal/steel/rubber/marking atlas; `LC_Wood` uses licensed native 2K Poly Haven timber derivatives. Labels are original inventory and handling marks, without fictional factions or borrowed logos. BaseColor is sRGB; normal and ORM are linear. OpenGL normal +Y; ORM = red AO, green roughness, blue metallic. No lighting highlights are painted into the albedo.

UV0 intentionally overlaps for reuse. Timber is metric, one repeat per 2 m, approximately 1024 texels/m. Metal faces use aspect-preserving panel fits within 492-pixel cells, with 10-pixel gutters; density varies by component size. The cylindrical shell follows cross-section arclength, and lettering has explicit atlas rectangles. See `Reports/uv_layout.png` and engine checker capture. These are shared trim/tile UVs, **not unique bake UVs**. No UV1, lightmap occupancy or high-to-low bake is claimed. Machined edges use restrained real bevel geometry; maps carry surface microdetail.

Six shared PBR maps cost approximately 96 MiB as RGB8 with full mip chains, or 128 MiB if expanded to RGBA8. Desktop BC7/BC5 storage is approximately 32 MiB with mips. Supplied import settings enable VRAM compression and mips, disable generated mesh LODs and retain authored LODs. These estimates exclude the diagnostic checker, resource overhead and driver residency; they are not measured VRAM use.

## Collision, gameplay and proposed integration

Scenes provide one simple closed-state box proxy for each crate and a cylinder proxy for the drum. Small handle gaps, the accessible interior and moving lids are not represented by collision. Opening a lid does not update the collider. GLBs provide render meshes and metadata; wrappers supply collision. Shape dimensions and actual transformed render bounds are recorded in the engine and GLB reports.

`Reports/integration_proposal.json` gives exact existing catalogue keys, future `gibs.file` paths and material mapping requirements. `loot-barrel-2` remains separate. The current crates are unbreakable take-only loot containers and vanish when emptied. The barrel is smashed for loot and cannot be opened in game. Damaged/open visuals are delivered for inspection and future reviewed presentation work. No fracture/gib GLBs or gameplay animation integration are claimed. The game gib builder requires new `MATS` entries for `LC_Metal` and `LC_Wood`, including suitable interior atlas UVs, before it can fracture these models.

## Rebuild and evidence

Use `Source/Rebuild.ps1` to rebuild geometry, validate exported bytes, compile C#, import, export scenes, validate the engine and capture the suite. All Blender and Godot jobs use the shared `Hollow_Production_Briefs_R01/run_heavy.py` mutex wrapper. `Source/prepare_textures.py` is optional texture regeneration and requires the recorded read-only Substation timber source; the final supplied maps suffice for geometry rebuilding. `Source/package.py` refreshes delivery hashes after inspection.

`Reports/glb_validation.json` checks actual GLB accessor bytes, bounds with transforms, indices, nonzero geometry/UV areas, normals/tangents, duplicate faces, outward orientation of closed components, hierarchy and budgets. Open label surfaces and assembled/open profiles are deliberate. `Reports/engine_validation.json` checks materials, colliders, use sockets, lid motion and exclusive LOD boundaries. `Reports/barrel_clearance_audit.json` provides the targeted damaged-shell audit. Engine import/build logs and captures are included. These checks do not prove arbitrary mesh self-intersection absence or artistic approval.

`Reports/provenance.json` records imported source URLs, CC0 licence records and hashes. The atlas, markings, geometry and scripts are original owner-controlled work. No paid generation or ImageGen was used. Bahnschrift was rasterized for labels; no font file is distributed. The local `game-dev` CLI was unavailable, so this is the directly validated Blender/Godot delivery requested by the brief, not a CLI canonical-package receipt. Final file hashes are in `Reports/package_manifest.json`.


## Drum lip revision - 15 October 2026

The upper rolled lip now has corrected winding and matching custom normals. The source correction is scoped to that surface; material backface culling is retained. Lid LODs are regenerated, so the remaining lid LOD triangulation also changes when the lip is excluded from collapse decimation. Body meshes and crate GLBs remain unchanged. Its circular profile uses 64, 32 and 16 circumference samples at LOD0, LOD1 and LOD2 so collapse decimation cannot fold the narrow cross-section.

Both intact and damaged drum exports contain 7,392 / 3,344 / 1,145 triangles at LOD0/1/2 (about 45.24% and 15.49% of LOD0). HANDOFF counts come from actual GLB index buffers. The standalone lip contributes 384 / 192 / 96 triangles. Blender pre-export metrics are diagnostic only.

Reports/lip_orientation_audit.json checks all lip triangles and corner normals in both states and all LODs. Reports/revision_scope_validation.json proves that all four crate GLBs and all six drum body LOD meshes are unchanged. The barrel wall-clearance values are carried forward from the archived report because body positions, normals, UVs, tangents, indices, materials and world transforms match exactly; the report records that evidence reuse.

In addition to the 20-view family suite, Renders/engine-lip-*.png provides 14 actual Godot captures: each closed drum and isolated raised lid above/below in both states at every LOD. Run View.cmd -- --lip-suite to repeat this evidence suite. The rolled crown is a one-sided upper surface and intentionally culls from below; the separate lid underside remains visible.

The rollback is Revisions/2026-10-15-pre-lip-fix/delivery-before-lip-fix.zip. The final archive is Archives/Hollow_LootContainers_R01_2026-10-15.zip, including that rollback and its verification receipt so the revision checks remain reproducible. See Reports/CHANGELOG.txt for the bounded change list.
