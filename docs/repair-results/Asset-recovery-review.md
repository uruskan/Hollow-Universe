# Asset recovery review — 16 October 2026

Read-only follow-up for the coordinated checklist review. This supplements
`docs/ASSET_LIBRARY_AUDIT.md`; it does not replace that earlier snapshot.

**Finding:** the relocated compound kit contains twelve real, textured, original-model
assemblies. The larger RustMVP transfer contains a mixture of usable sources, duplicates,
engine packages, supporting files and prohibited game-extracted content. Neither its file
total nor its byte-uniqueness result is a count of new usable game assets.

Only this document was written. No source assets or earlier audit were changed; no imports,
Blender/Godot runs, GPU jobs, builds, subagents, Git operations, admission or copying occurred.
An existing contact sheet was inspected. Checks below are local metadata/container inspection
and streamed hashes, not runtime, collider-fit or visual acceptance.

## Compound package: location, integrity and provenance

Canonical reviewed delivery folder:
`D:/GameDev/Assets/PSG_Compound_Utilities_R02`

Portable delivery:
`D:/GameDev/Assets/PSG_Compound_Utilities_R02_Delivery.zip`

The separate `D:/GameDev/Assets/new compound kit/` holds
`PSG_Compound_Utilities_R02_Working_Recovery.blend` and `Untitled.blend`. These are working
scene copies, not another twelve assets. R01 is an earlier revision. Prefer the explicit R02
delivery manifest for any later conversion.

Fresh checks:

- **228/228** paths, sizes and SHA-256 values in `Reports/Delivery_contents.json` match.
  Manifest SHA-256: `b1ebf046e82f590d3efd361a1461640060902b154c98721688649e3b9398dfea`.
- The ZIP is **2,324,102,338 bytes** and matches `Reports/Delivery_result.json`:
  `53066efc2a4eaf8c1aab000ff9fae2b333d5277251fc9619f00cdab35f4af406`.
- ZIP membership is exactly the 228 roster entries plus `Reports/Delivery_contents.json`:
  **229 members**, no missing or extra member beyond the manifest itself. No extraction ran.
- The expanded working folder has **472 files**. The extra development/vendor files are not
  all in the portable delivery roster; this does not give the entire working tree a closed receipt.
- Fourteen production Blender files exist: two master files and twelve `Source/GameReady/`
  asset files. Two additional `.blend` files are material-source dependencies, making sixteen
  `.blend` files in the whole working folder.
- **12 GLBs, 66 FBXs, 48 baked PNGs** are present. The FBXs represent **22 rigid components
  at three detail levels**, not 66 unique asset designs. The baked PNGs are BaseColor,
  NormalGL, NormalDX and ORM for each assembly; seven assets use 4K, five use 2K.
- All twelve GLB headers, lengths, buffer-view ranges and triangle-index count divisibility
  are structurally consistent. Render primitives have POSITION, NORMAL and TEXCOORD_0;
  embedded images and material bindings exist. No external image/buffer URI is needed by
  these GLBs. This is not an exhaustive vertex, topology or shader test.

`README_Import_and_Assembly.md` records original modeled assemblies and explicitly excludes
extracted Facepunch meshes. `Reports/Texture_sources.json` records eighteen source rows
(including repeated material entries), with Poly Haven and ambientCG URLs and CC0-1.0 labels.
The rope is documented as procedural PBR, not a photograph. These are local provenance records,
not a new online licence investigation or a signed independent authorship certification.

The saved final QA reports claim completed export/round-trip and pivot checks; the older
`Asset_manifest.json` status still says export QA is in progress. Final delivery hashes are the
better version anchor, but no old QA claim was treated as a newly run engine test. `game-dev`
was not found on PATH. This is a project-specific delivery/ZIP verification, not canonical
Game Development Studio admission.

## Actual assets and integration implications

All paths below are under the R02 folder's `Exports/GLB/`. Triangle counts are freshly read
from actual exported index-accessor counts, including all render components once.

| File | Actual LOD0 triangles | Appropriate use / required work |
|---|---:|---|
| `01_Large_Furnace.glb` | 94,680 | Large furnace visual; shared fuel/cooking rules, placement, interaction and FireFX/SmokeFX integration |
| `02_Wooden_Barricade.glb` | 10,616 | Short defensive obstacle; collision, placement and current character jump/eye-height checks |
| `03_Wood_Compound_Wall.glb` | 14,826 | Compound perimeter with snap anchors; shared placement, claims, damage and persistence |
| `04_Wood_Compound_Gate.glb` | 74,347 | Frame plus two leaves; preserve hinges, moving collision, locks/interactions and network state |
| `05_Stone_Compound_Wall.glb` | 19,126 | Perimeter wall; damage/placement and optional barbed-wire hazard rules |
| `06_Stone_Compound_Gate.glb` | 46,568 | Articulated compound gate; same shared gate requirements |
| `07_Windmill.glb` | 46,427 | Confirmed wind-generation direction; separate tower, yaw head and rotor, power-output socket; no electricity simulation supplied |
| `08_Orbital_SAM.glb` | 192,011 | Six launch sockets plus yaw/elevation; potential future defence content, not an already approved/implemented weapon system |
| `09_Wood_Spike_Barricade.glb` | 18,228 | Defensive obstacle; contact-damage behavior is game-side |
| `10_Metal_Barricade.glb` | 10,468 | Defensive obstacle; collision/damage/placement integration |
| `11_Auto_Turret_Gunless.glb` | 85,186 | Strong match to confirmed installed-weapon turret design: base/yaw/pitch, WeaponMount/RearGuide, no gun or ammunition; needs power, authorization, inventory and attack integration |
| `12_Wooden_Ladder.glb` | 22,028 | Wall-placeable ladder with Climb_Start/End; climbing and claim exception remain gameplay work |

**Format distinction:** the twelve GLBs contain **only LOD0 render meshes**, with
their articulated hierarchy and **45 socket nodes**. They contain **no LOD1/LOD2 mesh nodes,
no UCX collision meshes and no animation clips**, and no LOD extension supplying those features.
The complete package supplies lower LODs and collision through the FBXs and Blender sources.
All 22 LOD0 component FBXs have named UCX entries; fresh binary-name inspection finds **90
distinct hull names**, matching the delivery totals. This establishes named data presence,
not a fresh convexity or collision-fit validation.

Missing authored LOD1/LOD2 inside a GLB is **not an integration blocker**. The project uses
Godot's automatic mesh LOD (`game/scripts/Settings/GraphicsSettings.cs:417` sets
`MeshLodThreshold`); new rigid props can be imported with automatic LOD generation enabled,
preserving separate moving meshes. Authored FBX LODs are optional quality fallbacks or
references, not a reason to convert every FBX. This is mesh LOD, not evidence of a custom
Nanite-style virtualized geometry renderer. Some current environment assets deliberately use
authored levels with `meshes/generate_lods=false`, including the active cliff GLTF imports.

Automatic mesh LOD does not import the separately supplied UCX collision or implement socket
behavior. Transfer collision through the project's shared engine-free representation, retain
the GLB socket hierarchy, and validate gameplay fit. Do not flatten the turret, windmill or gate
leaves into a static whole. The source uses metres, X forward, Z up; GLB is
already converted to glTF Y-up. Apply the adapter's axis convention once.

The actual twelve GLBs total **634,511 triangles**, 105 fewer than the source QA total 634,616.
The differences are confined to five exported assets and are also disclosed in the transfer
report. They are not evidence of missing asset families. The source reports' LOD1/LOD2 totals
are 272,869 / 101,520, but those are not contained in the GLBs.

The large texture atlases and turret/SAM/furnace mesh counts make LOD and material preparation
relevant to P8 performance work. No frame-time claim follows from static counts. The source
ladder is about 3.117 m high; compound gates are about 5.47–5.80 m high and 6.08–6.41 m wide.
These require deliberate fit checks rather than assuming the existing 3 m building aperture.

No references to this package or its exported asset identifiers were found in the inspected
active importers, ground data or simulation source. That is an inspection-time integration
finding, not a guarantee against concurrent changes. The existing window-insert gap from the
earlier audit is separate; this compound package does not supply those missing gameplay rules.

## Recovered HTML inventory: what its numbers mean

Inspected as text/JSON only; no embedded JavaScript was executed:
`C:/Users/uruskan/Documents/Codex/2026-10-16/it-x20/outputs/RustMVP_File_Inventory.html`

HTML SHA-256: `5353433f60b11415804e1d69278fbca7167a0b4907604efd03ccd47259a8d823`.
The same transfer's reports are under
`D:/GameDev/Assets/Inventory/RustMVP_Asset_Transfer_2026-10-16/`.

The HTML records **5,812 rows: 5,684 copied and 128 already-identical skips**, totalling
23,680,079,058 copied bytes. All 5,812 destination paths are within `D:/GameDev/Assets`, exist,
and match their recorded sizes in this review. Full transfer hashes were not all rerun.

| Copied-file category | Count | Meaning |
|---|---:|---|
| Unreal packages | 3,592 | 3,581 `.uasset` + 11 `.umap`; mixed content, not directly loadable Godot art |
| Texture/image | 1,171 | Includes real maps, website artwork, previews and excluded Rust textures |
| 3D asset/source | 320 | 51 GLB, 106 GLTF, 106 FBX, 39 Blender, 12 Blender backups and six binary buffers; LODs/revisions/components are counted separately |
| Supporting files | 566 | Includes 204 Python files, 142 JSON files, logs, configuration and other data |
| Archives | 21 | Contents overlap unpacked packages; not 21 extra asset collections |
| Fonts | 11 | Includes repeated Barlow/OFL sources already used by the ground UI |
| Audio | 3 | Soundtrack derivatives, with licence unresolved below |

The prior duplicate scan reported 831 copied files byte-identical elsewhere and 4,853 without
a match. It did not decompress the other archives and cannot establish semantic uniqueness,
licensing or suitability. It also records 174 redundant copies within the transfer. Do not
translate any of these numbers into "new recovered assets."

Fresh additional checks: **209 selected files** across non-Oxum SourceWorld, SourceTerrain,
PlanetRecipes and SourceUI match the HTML hashes; **all 191 AdditionalSources files** also match.
The selection is 400 files in addition to the compound roster. All **110 copied/skipped GLTFs'
485 external buffer/image references** resolve locally inside the asset library, with no
network URI. Resolution establishes portability, not clearance; it includes excluded Oxum.

## Concrete recovered candidates and exclusions

| Location under D:/GameDev/Assets | What it actually supplies | Judgment / repair scope |
|---|---|---|
| `PSG_Compound_Utilities_R02/` | Twelve original-model assemblies detailed above | New building/deployable/electricity work; GLBs can use automatic import LOD, with separate collision and gameplay integration |
| `PSG_FrontierAK_R05/` | Original rifle model, textures, sockets, mechanical rig/weapon clips | Already the source of `art/import/frontier_rifle.py`; useful P4 reference, not a newly missing rifle or hand animation set. R03/R04/CleanADS are revisions, not extra required weapons |
| `RustMVP_AdditionalSources/work/planet/ecosystem-20261005/vegetation/` | **36 variants across 12 plant families in 105 GLTF exports** | Practical P2 candidates. Most have three LODs; all three bracken-fern variants have two. See palette metadata and source/entitlement caveat below |
| `RustMVP_AdditionalSources/work/planet/art-resume-20261003/fir/export-v2/` | Four FBXs: fir A/B at LOD0/LOD2, seven texture maps and export records | Existing LOLIPOP source derivatives; potential P2 diagnostics/variants. Preserve attribution and channel conventions; not four new tree species |
| `SourceWorld/vegetation/` | Selected grass, fern, dwarf-birch, willow and fir GLBs/maps with source listings | Real environment candidates; many duplicate older library exports. P2: compare to current native-UV imports before selecting |
| `SourceWorld/rocks/` | Nordic cliff, desert cliff, forest boulder GLBs/maps and provenance | P1 candidates only after source/scale/LOD review; existing geology already has related sources |
| `SourceWorld/extras/pine_forest/` | Fallen log, roots, stump, grass, moss boulders and mature pine exports | P2/P1 decoration candidates. Pine is already referenced by the current tree importer. Local provenance explicitly says the exact archive origin was not independently authenticated |
| `SourceWorld/extras/namaqualand/` | Didelta and othonna shrubs | Real dry-biome meshes; provenance likewise discloses unauthenticated exact archive origin |
| `SourceWorld/birch/` | LOLIPOP five-birch GLB and maps, explicit CC BY 4.0 attribution | Already-related tree source; retain attribution and distinguish unused variants from a missing pack |
| `SourceWorld/surfaces/` | Seven processed Quixel surface families with map hashes | P2 material alternatives, often already represented by existing layers; no automatic terrain fix |
| `SourceWorld/nodes/` | Three older node GLBs labelled user-created in provenance | Current four-family, three-stage PSG replacement already exists. The old recipe marks these `held_placeholder`; no reason established to replace current repaired nodes |
| `PlanetRecipes/` | Seven JSON recipe/review/validation files | Design/reference data, not generated meshes. `temperate_coast.recipe.json` explicitly says it generates neither geometry nor placements and grants no asset approval |
| `SourceTerrain/` | 769×769 float32 flat heightfield, biome masks, previews and metadata | Existing terrain data/reference, not a spherical-planet repair. Earth worker owns generation/save identity; do not substitute this old 1,536 m-wide patch |
| `SourceUI/`, `RustMVP_AdditionalSources/website/public/`, `.../work/site-art/` | Fonts, menu/website imagery and prompts | UI/reference/promotional material, not models or terrain textures. AdditionalSources has 45 website and 17 site-art images; ground UI preservation still applies |
| `service_pistol_4k.blend/` and loose material/model ZIPs | Actual source geometry/maps and packed downloads | Further candidates only; acquisition/author/licence must be established individually. Filename alone does not clear a source or justify replacing the current pistol |

The ecosystem palette's twelve families are rhazya, wild grass, dry grass, tundra grass, dead
shrubs, bracken fern, wood sorrel, cotton grass, bilberry, alpine bearberry, downy willow and
dwarf birch. It points to twelve existing `*_ue_high/standard/*_nonUE.gltf` sources. **All twelve
source hashes still match** the palette. These are prepared derivatives of existing library
sources, not newly acquired plant rights. Recorded Fab ownership/source receipts should be
carried forward and applicable cross-engine entitlement confirmed for each selected family.

Explicit exclusions / unresolved provenance:

- `SourceMonuments/oxum/` (348 copied files) and `SourceWorld/oxum/` (350) are the Rust gas
  station geometry/textures. The latter's `PROVENANCE.json` explicitly says conversion from
  installed Rust `gas_station_1.prefab` via UnityPy, not original/Fab content. They are excluded
  by the project's no-other-game-files rule, regardless of having valid hashes.
- `RustMVP_ProjectContent/Content/Rust/` and the `BareRustWorld*` maps remain excluded.
  The broad native Content backup also mixes PSG/Tin Finch imports, third-party character
  material, KiteDemo, World Partition actors and old world data. It is not a cleared asset pack
  or a portable Godot project. Do not transplant `Content/World` wholesale.
- `RustMVP_AdditionalSources/work/planet/ecosystem-20261005/BareRustField.eco` and its field
  metadata describe native terrain/water/biome channels. No independent original-map provenance
  was established here. Exclude this field from recommendations; the vegetation derivatives can
  be assessed separately through their own source records.
- `audio/bare_soundtrack/` contains Full, Loop and loop-reference WAVs. Its provenance points
  to `videoplayback.m4a` and explicitly states **no licence supplied or inferred**. These are not
  cleared music replacements or recovered footsteps/effects.
- The new inventory cannot clear the seven animal packs flagged in the earlier audit.

## Handoff and remaining validation

P1 geology and P2 environment can use the exact candidate paths above as alternatives, while
preserving their current source/material/placement ownership. P4 can inspect already-used
Frontier rifle sources without reverting current optics or animation work. P8 should account
for full LOD/material cost before any compound assets enter a repeated player-built scene.
Compound utilities need a separate owned implementation scope; their presence does not close
electricity, turret, gate, ladder, damage or persistence checklist items.

The highest-confidence newly located asset delivery is R02: its reviewed roster and ZIP match,
and actual geometry/textures are present. Integration should begin with a concrete asset selection,
coordinate/collision plan, automatic import LOD quality checks and normal project admission checks.
Use authored LOD sources only where their quality warrants it. No new authorization to admit
assets, alter systems, or claim completed gameplay is supplied by this report.
