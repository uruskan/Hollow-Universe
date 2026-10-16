# Authored tree replacement — candidate installed 16 October 2026

## Scope and cause

The original surface library still built every fir, pine, birch and broadleaf
trunk and crown from procedural geometry and a painted foliage atlas. Better
ground texture or grass coverage could not fix those tree silhouettes or bark.
This package replaces three families with user-supplied authored models: three
fir variants, three birch variants and one mature pine. Broadleaf remains the
existing procedural oak/beech and is an explicit remaining art limitation.

The existing scatter, object IDs, random placement, spawn rules, yields,
respawn, collision and gathering data are unchanged. The library is the common
source for standing, struck, falling and distant trees, so the same new mesh is
used by those existing paths. The candidate is installed and imported. The
final independent pine geometry audit and runtime chopping/fall checks pass.
Paired representative-route measurements record a woodland frame-time
regression; forced LOD views and transition acceptance remain pending.

## Sources and derivation

- LOLIPOP Fir trees pack: the supplied `christmass_trees_pack.blend`, with
  original UVs, packed albedo, tangent normals and metallic/roughness/AO maps.
  Three mature variants use the creator's LOD0 and LOD2. MRAO is explicitly
  repacked to glTF ORM without artistic recolouring.
- LOLIPOP Five Birch trees pack: supplied GLB; three variants use the creator's
  LOD0 and LOD2, original bark/leaf UVs, albedo and tangent normal maps. The
  supplier's scalar roughness is retained where no roughness map exists.
- Poly Haven pine forest: supplied `polyhaven_pine_fir_forest.blend`, via the
  existing documented `mature_pine_a.glb` derivative in the user's RustMVP
  project. That derivative preserves `pine_02`'s authored branch arrangement,
  scanned bark and seven original twig photographs baked into crossed twig
  cards. It contains no Facepunch Rust art/code. Its prior receipt identifies
  the supplied archive and notes that its exact archive origin was not
  independently authenticated; the user authorized these local assets.

`game/assets/surface/trees/catalog.json` records the original/derivative source
paths, SHA-256 hashes, derivative receipt, each exported dependency's hash,
triangle counts, bounds, stem fitting and measured radial samples. Original
libraries and the reference project were not modified. Runtime admission is
limited to catalog-referenced dependencies. The converter also has one
documented, versioned stem seed, described below; scratch exports are excluded.

`tools/blender/environment_trees.py` is a CPU-only Blender conversion. It does
not execute embedded scene scripts, render, download or call providers. Fir's
complex Blender-only BSDF is represented by its native PBR maps and restrained
runtime transmission, rather than exporting an unsupported node graph as a
flat material. Pine leaf normal maps are neutral because its existing twig
photograph bake has no normal attachment; its geometric normals are preserved.

Pine's three bark materials retain their supplied roughness images, sampled
from the glTF green channel with factor 1 and metallic factor 0. The exporter
now follows Blender's Separate Color node instead of substituting the unused
Principled socket default when a scalar map is connected indirectly. An
unsupported scalar graph fails explicitly. Pine needles retain their source
alpha cutoff of 0.32. The six previously imported pine albedo/normal image
payloads are unchanged by the repair.

## Dimensions, collision and cost

Visual target heights preserve the old library's combined geometry × species
scale: fir 18.2 m, birch 15 m, pine 19.2 m, before the unchanged scatter scale.
These dimensions are now baked and the new species scale is 1. The existing
gameplay capsules still have heights 12/11/14 m respectively; this package does
not pretend those inherited capsule heights were identical to the old crown
heights. Their radii remain .36/.24/.38 m.

The lower stems are centred and fitted independently of their crowns, using
closed cross-sections every 20 cm. Fir/birch fitting blends out by 4 m. The
pine's original 4 m transition visibly pinched above the chopping band, so its
upper stem now tapers gradually through 8.3 m. No pine position at or below
2.1 m changes in this repair; the measured gameplay radius, ground alignment,
root flare and native UVs remain stable.

`tools/blender/source_geometry/pine_chop_stems.glb` contains the two previously
validated trunk primitives as an immutable conversion seed. Its adjacent JSON
records version 1, original pine source/hash, prior generated geometry hashes,
fitting metadata and the seed's SHA-256. This derived resource is bundled with
the converter and read relative to the script after hash verification. A clean
checkout does not need `.artifacts` or an already installed generated tree to
export. It contains our derived stems from the same authorized source, not
another game's art.

Near pine retains every original branch component and all 2,210 photographed
twig cards. Medium pine selects complete substantial source branches and
1,193 complete twig cards, preserving their triangles and atlas UVs. Crown
collapse decimation is no longer used. The converter enforces a 26,000/8,000
near/medium triangle ceiling and rejects an upper-branch triangle over 0.02 m².

| Family | Variants | Near triangles | Medium triangles |
|---|---:|---:|---:|
| Fir | 3 | 12,064–18,351 | 4,670–5,768 |
| Birch | 3 | 10,039–17,354 | 5,237–9,121 |
| Pine | 1 | 25,776 | 7,521 |

Pine near/medium measured heights are 19.200/19.126 m, with ground minima 0.
The extra near triangles are a deliberate cost of preserving actual branches.
The rejected 13,667/6,000-triangle pine was cheaper but visibly damaged its
source geometry. Whole-candidate runtime measurements below report the cost
honestly; they do not isolate this mesh's contribution from other changes.

The existing 50 m/190 m bands, quality scaling, shadow distances and far
impostor machinery remain. Far trees use eight 256 px views per variant. The
native materials share their source textures across their LODs and variants;
there is no per-instance material allocation. These are asset measurements,
not an in-game performance result.

## Rendering integration

`FloraLibrary.Trees.cs` validates that exported accumulated node transforms are
identity, then preserves each surface's native albedo factor, alpha cutoff,
normal strength and roughness/AO channels. The existing geology
`LoadAuthoredRocks` integration is retained.

The old impostor baker identified only four shared procedural material objects
and treated unknown surfaces as procedural bark. The updated baker caches by
actual source material, retaining native albedo and leaf alpha for every view.
Its authored row range is passed through one Vegetation initialization
parameter. Only those far rows receive the native neutral tint/transmission;
procedural broadleaf rows keep their existing treatment.

The new tree shader retains spherical instance transforms, height-weighted
wind, tool shake, LOD dither and atmospheric scattering. Direct and environment
reflection use a consistent weight with native roughness. Its per-light cloud
shadow call requires the accompanying lighting package's two-argument
`hu_cloud_shadow` include in the same activation checkpoint.

Trees and far impostors use explicit alpha discard, without ALPHA or alpha
antialiasing built-ins. This keeps colour and depth in the same opaque pass so
refracting water cannot read a tree's depth while omitting its colour. Texture
mipmaps, explicit material cutoffs and existing LOD dither remain. The tradeoff is loss
of alpha-to-coverage edge smoothing; actual MSAA/water captures must assess it.

## Rejected first runtime pine and correction

The first High representative-route frames, `candidate-route-high/00-beach.png`
and `01-woodland.png` under `.artifacts/environment-pass`, exposed defects that
the capsule-ray checks had not detected: a bottle-shaped lower trunk, large
solid triangular branch fragments and a sparse medium canopy. These frames
were rejected as pine art validation.

Inspection separated the causes:

- Global collapse simplification deformed disconnected branch geometry. The
  largest upper-branch triangles reached 1.67 m² near and 2.04 m² medium, versus
  about 0.013 m² for the correctly scaled source. Complete branch preservation
  reduces both candidate maxima to 0.013 m².
- A final whole-crown simplification reduced medium leaf geometry from the
  near 4,420 triangles to 357 and damaged atlas-card silhouettes. Medium now
  contains 2,386 triangles in complete two-triangle cards.
- The fitted 0.38 m lower radius shrank to the much thinner scan too quickly
  above 2.25 m. The longer upper taper removes that abrupt transition while
  preserving the measured lower geometry.
- Pine roughness maps were dropped by incomplete node traversal, leaving a
  uniform factor 0.5; the generic leaf export also replaced the source cutoff
  0.32 with 0.42. Both source material properties are restored.

Matched CPU orthographic geometry/alpha silhouettes show the branch fragments
removed, canopy coverage restored and the trunk transition improved. They are
diagnostic projections, not game renders, lighting approval or FPS evidence.
The root accepted that geometric improvement for the next runtime comparison.

## Validation status

- Final pine CPU export succeeded; the converter fails if any of 16 radial samples
  at .7/1/1.5/2 m misses or differs from the gameplay radius by over 25 mm.
- The first independent review parsed the actual exported GLTF/bin: all 14 meshes ×
  64 rays = 896 hits. Every sampled normal and triangle winding points outward.
  Maximum measured radius error: fir 11.5 mm, birch 9 mm, pine near 20.6 mm,
  pine medium 15.3 mm. UV0 exists, all attributes are finite and output hashes
  match the catalog. This was an initial geometry check, not proof of visual
  correctness; it did not detect the subsequently rejected crown silhouette.
- The [final installed-pine independent audit](images/repairs/environment-2026-10-16/pine-independent-audit.json)
  parses actual GLTF/binary data independently of Blender. It passes 128/128
  stem rays across both LODs; every sampled normal and triangle winding points
  outward. Maximum radius errors are 20.602 mm near and 15.292 mm medium.
  The 2,498/1,305 protected lower-stem vertices match the versioned seed exactly
  (maximum position difference 0); all stem UVs and indices are identical.
  Near preserves all 14,238 + 2,306 source branch triangles; medium's 2,180 +
  450 triangles are exact source position/UV/winding subsets. All 2,210/1,193
  leaf quads are complete source quads, with zero partial quads. Attributes are
  finite, normals are unit length, and the largest upper-branch triangle is
  0.012621 m². All eight installed payload hashes and twenty protected
  dependencies match. These are final independent checks, not the earlier
  exporter's reported values.
- Independent material review confirmed source roughness PNG payloads,
  green-channel sampling/factor 1, preserved metallic factor 0 and source
  cutoff 0.32. The combined package's hash preflight checks eight changed files
  and twenty unchanged dependencies, including all fir/birch assets.
- Source review confirmed native material channels,
  accumulated transforms, radial wind conversion, native-row distinction,
  alpha discard and the existing falling-tree library seam.
- The root completed candidate compilation with no warnings/errors and
  installed/imported the combined pine correction cleanly. Both final High/day
  and Maximum/night routes technically completed with all six checkpoints
  ready. Their images and raw measurements are preserved in
  `docs/images/repairs/environment-2026-10-16`.
- The normal child-server `candidate-pine-chop2` run completes and is accepted
  for the X-hit chain, depletion and falling/landed sequence. Its preserved
  [runtime log](images/repairs/environment-2026-10-16/candidate-pine-chop2.stdout.log)
  records X1 through X8, `depleted=True`, final-hit wood 125, and saved falling,
  landed and chopped frames before `[tour] done`. This is an actual hosted
  gameplay check, separate from the radial geometry audit.
- Forced LOD0/1/2 captures at 45 m completed with ready frames, exit 0 and
  empty stderr. Originals and launch/log records are in
  `docs/images/repairs/environment-2026-10-16/flora-lod`. All new tree types
  render with cutout silhouettes in all three modes. Medium birch is visibly
  sparser, and far foliage is brighter than the near meshes. Those differences
  still need tuning. This surface-view lineup is presentation evidence, not
  the actual Earth1200 gameplay route; it does not verify the moving 50/190 m
  transitions or water/refraction behavior.

## Paired runtime cost

The [matched route comparison](images/repairs/environment-2026-10-16/comparison.md)
uses the same planned route and matching graphics/run settings. Woodland
median frame time rises from **15.50 to 17.68 ms at High/day**, and from
**28.04 to 36.20 ms at Maximum/night**. These are regressions, not performance
wins. The measured environment candidate includes trees and other environment
changes; the pair does not establish the isolated cost of the pine repair.

Each profile samples at most 20 seconds from its leg's start; the woodland
samples cover about 8.4 seconds. These are bounded route measurements, not
cold-load timings or an overall FPS guarantee. The comparison records sample
counts, percentiles and checkpoint differences; VRAM telemetry is unavailable.
Performance optimization is owned by the separate performance workstream and
remains outstanding. Preserving source geometry does not waive that work.

## Activation and import

The initial source package and combined pine correction have been applied with
baseline/hash checks. For subsequent rebuilds, `surface_impostor.gdshader` incorporates the
lighting package's current-light cloud fix; do not overwrite it with the older
lighting-only copy afterward. Copy only model/image/bin files referenced in
the catalog plus the catalog itself. Preserve the versioned conversion seed as
a tool input and preserve unrelated geology/audio/held work. Source receipts
in `THIRD_PARTY.md` must retain the existing geology additions.

After the first coordinated Godot import, run
`python tools/blender/environment_tree_import_settings.py game`, then import
again. This disables redundant generated mesh LODs and enables GPU-compressed
mipmapped textures with correct normal-map import classification. Do not run
the large native textures with uncompressed/default imports and call the
result a representative performance measurement.
