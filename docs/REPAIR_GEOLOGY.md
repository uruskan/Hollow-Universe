# Scanned rocks and connected cliffs — 16 October 2026

The owner rejected the smooth, flattened procedural boulders and supplied Rust's
World Update 2 as an environment reference. This bounded pass replaces ordinary
rock art and improves the existing shared cliff placement system. It does not
claim to finish grass, trees, terrain materials, water or the overall environment.

## Reuse from RustMVP

Read `RustMVP/tools/planet/rust_reference_preview/prepare_local_cliffs.py` and
`local_cliffs.py`. Their local replacement path uses Nordic/Tundra scans from the
owner's asset library, preserves source UVs, base colour, ORM and tangent normals,
bakes transforms and records hashes. This port adapts that approach to Godot and
reuses two of its Nordic source meshes, with a lower forest ledge from the same
library. Unreal packages, Nanite code, Rust geometry and recovered map placements
are not portable inputs to this implementation and were not copied.

## Runtime changes

- `FloraLibrary` loads nine real mesh variants: seven Poly Haven moss stones and
  two owner-supplied Fab tundra boulders. Eighteen near/far meshes retain scanned
  UVs and PBR textures. The ordinary scatter path no longer calls the procedural
  ellipsoid generator. Harvestable ore artwork is a separate existing system.
- Rocks keep their original proportions and fit the old gameplay envelope by
  uniform scaling. Their existing approximate spherical gameplay colliders are
  retained. This is **not triangle-exact rock collision**; close edge contacts can
  still differ from the scan. Placement IDs, scatter positions and yields are unchanged.
- A dedicated scanned-rock shader preserves source maps, cloud shadow, atmosphere
  and complementary instance fades. It does not paint generic moss over the scans
  or shrink rocks at their draw-distance boundary.
- Cliff candidates form up to five overlapping contour-following columns, each
  with up to five descending rows. The owner's follow-up asked for the same cliff
  meshes to fill the bare face vertically. Rows overlap, follow local radial up,
  and stop at water, a valley/slope reversal, flatter ground or a monument clearing. Each
  member has independent radial orientation, terrain fitting, monument exclusion
  and a stable `(tile, part)` identity. Gentler sites select broad forest ledges;
  steeper sites select broad/tall Nordic faces. Candidate positions remain seeded.
- The same transformed scan collision triangles are used by server, prediction
  and rendering. Near queries include the neighbour offset so components remain
  discoverable across source-cell boundaries. This changes generated cliff output
  deliberately; planet heights, stars, natural-object IDs and saves are not regenerated.
- Model resources stream on background loader requests for nearby cliff families.
  External shared texture files let Godot import/compress maps once for both LODs.
  Four-kilopixel maps are retained; near geometry is bounded at 24,000 triangles per
  cliff, far at 3,500, collision at 700. Ordinary rocks are 6,500/900 triangles.

## Sources and reproducibility

Run Blender 5.2.1 in background mode with `--python-exit-code 1` and
`tools/blender/environment_rocks.py` / `environment_cliffs.py`. Inputs remain in
`D:/GameDev/Assets` and are never modified. The catalog/receipt files record source
and dependent texture/buffer SHA-256s, output hashes, source material rights and
normalization. Fab assets use the owner-supplied library basis already documented
in `game/assets/THIRD_PARTY.md`; they are not CC0. No paid provider or download ran.
After the first Godot import, run `python -I tools/blender/geology_import_settings.py`
and import again. Checked-in `.import` settings enable GPU compression and mipmaps
for all 24 maps and disable redundant automatic mesh LOD generation.
The optional game-dev CLI was unavailable; these are project-native conversion
receipts, not canonical game-dev package admissions.

## Validation and limits

Implementation was developed in an isolated checkout to avoid the concurrent main
repair work. GeologyPort, EarthCliffs and Architecture checks exercise real exported
mesh/material data, hashes, LOD bounds, deterministic multipart generation, part
identity, monument clearance and shared collision/streaming. Thirty actual Earth
formations / 402 components are checked, including 24 tiered faces; Earth has 73
nonempty candidate groups. A regression check rejects lossless rock texture imports.

Initial live captures exposed a loading timeout and then an incomplete export.
Those failed captures are not visual acceptance. Incorrect lossless texture imports
also caused a Vulkan device-loss failure; GPU-compressed maps and asynchronous cliff
resource loading are part of the fix. The final isolated solution build has zero
warnings/errors; all eight focused checks pass. The actual Earth scene renderer
completed its cliff and rock captures with streaming ready, without renderer errors.
`game/scenes/GeologyReview.tscn` reproduces those checks (`--rocks` selects the rock
view; `--shot` names the output). Full hosted-game and integration results are recorded
in DEVELOPMENT.md. The existing four legacy cliff GLBs are retained but unused by
the updated renderer; only the external glTF resources are active.

Further work needed for the reference quality: varied talus/debris distribution,
soil/vegetation pockets that follow these exact meshes, biome-specific geology,
formation density/composition review around the whole planet, and precise ordinary
rock colliders. Those are not presented as completed by this port.
