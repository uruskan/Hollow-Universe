# P1 Cliff formations and geology

Repair N1/N4/N5, R2/R11 and the geology part of L3 in AB Test B. Read COMMON.md,
REPAIR_BACKLOG.md, REPAIR_GEOLOGY.md and original screenshots 1, 2, 5 and 6. The owner rejects
isolated repeated cliff fragments, naked steep faces and the repeating vertical wall.
Produce coherent natural formations covering the intended exposed faces, with varied scale,
silhouette and transitions. Optimize actual formation cost while retaining visual fidelity.

Ownership: `src/Hollow.Surface/CliffPlacements.cs`, `CliffGeometry.cs`, `Data/cliffs.json`;
`game/scripts/Surface/CliffField.cs`, `Flora/FloraLibrary.Rocks.cs`;
`game/shaders/surface_scanned_rock.gdshader`; `game/assets/surface/cliffs`, `rocks`;
`tools/blender/environment_cliffs.py`, `environment_rocks.py`, `geology_import_settings.py`;
new or directly corresponding geology tests. Confirm actual paths before edits.

Do not edit WalkingWorld/WalkingParameters or terrain-save identity: the active “Check Earth
planet surface area” chat owns them. Investigate screenshot 5 and hand off any terrain-height
cause with a reproducible coordinate. P2 owns terrain/grass shaders and cover; coordinate
formation masks/vegetation pockets instead of editing its files. P3 owns harvestable nodes.

Suggested three-agent split:
1. Read-only source/asset/reference recovery and geometry/texture cost audit, then owned export assets.
2. Deterministic cliff placement, terrain fit and shared collision, with behavior tests.
3. Renderer/LOD/streaming and reproducible visual/performance diagnostics.
The lead allocates exact files between them and reviews integrated composition.

The current library already has Nordic/forest scans, plus prepared coastal/Namaqualand assets.
Use receipts and inspect sources; prior work was not wholly lost. Do not increase instance count
blindly. Larger modules, fewer well-fitted parts and material blending are candidate techniques,
not mandated implementations. Preserve texel density, collision, seed stability and streaming.
No assets from other games. No automatic purchase/download scope.

Acceptance: actual hosted close/medium/distant ground views at top/face/base, different slopes
and biomes, LOD transitions, collision/raycast agreement, no floating spikes or uncovered
repetitive dirt walls. Capture the reported locations if recoverable; otherwise record exact
reproduction sites. Compare CPU/GPU/geometry/instance costs at matching settings and times.
Changes to generated output need intentional golden updates and commit notes. Coordinate
build/import/GPU execution; do independent asset/code work while the slot is occupied.
Write `docs/repair-results/P1-cliffs.md`; preserve rejected trials and state remaining limits.
