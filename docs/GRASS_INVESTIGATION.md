# Grass rendering investigation — 18 October 2026

**20 October 2026:** the grass is rebuilt (clumps of five species on worker-built tiles, one habitat rule, roots on
the drawn triangles, real bent normals with a bounded glow): [WALKING_GROUND.md §18](WALKING_GROUND.md). The audit
below describes the field it replaced.

## Visual regression target added 19 October 2026

The owner explicitly compared current playable grass unfavorably with the loading
screen. Root inspected both `game/assets/loading/golden-meadow.jpg` and the native
hosted High capture `.artifacts/environment-ownership/hosted-high-r21/ground.png`.
The loading picture is a real historical Godot surface-view render, introduced in
commit `4d4565e7`, not unrelated concept art. GROUND.md records a 2560×1421 render
of look-dev view (b): latitude44.86225, longitude−170.00150, altitude1.7m,
yaw280°, pitch0°, time2026-10-12T15:50:00Z. Its older planet/generator is not the
present playable WalkingWorld; matching that camera command does not recreate
identical geometry or climate. The source image remains immutable.

The reference has a coherent dense meadow, small flowers, variation in grass
height/color and strong grazing backlight. The current High hillside capture has
obvious exposed gaps, spiky repeated upright silhouettes and hard dark/light
contrast. This is not explained by the user's saved Low settings: r21 explicitly
used High. The main layer's `blade_underlayer` now disables its flower/undergrowth
selection and halves transmission, while the separate authored layer owns those
species. The old and current tuft base geometry both use five three-segment cards;
current contact, deformation, normal, coverage and world sampling paths differ.
These are verified differences, not an isolated causal ranking yet.

Acceptance now includes native comparisons of the historical camera/time command,
and actual playable meadow/forest-edge/hillside scenes in ordinary daylight and
golden hour, with matching resolution and settings stated. Recover the reference's
coverage, silhouette variety and coherent lighting while retaining deterministic
contact and bounded spatial batches. Do not accept a sunset-only improvement or
count native correctness tests as aesthetic approval. GPU cost still needs paired
measurements after a visually acceptable candidate exists.

**Historical comparison, 19 October.** The historical command stopped producing grass after the
drawn-contact change: the full-size look-dev planet's finest terrain level is 18, beyond the atlas's
level-nine ceiling, and the field threw on every frame. The field now refuses contact once for such a
surface and keeps heightfield placement. The command renders again in 15 s without engine errors:
[then and now](images/grass-regression/historical-view-b-then-and-now.jpg), 2560 × 1421, Maximum.
Terrain, camera and time match. Today's grass is darker and bluer, barely glows against the low sun,
and exposes soil between tufts; its pink flowers are gone. The trees and the boulder also differ,
because authored trees and scanned rocks replaced the procedural ones on 16 October.

At the hosted landing site, the same High view was rendered with `blade_underlayer` switched off for
one run only (source unchanged afterwards), and with all ground cover hidden:
[landing-site sheet](images/grass-regression/landing-site-underlayer-and-ground.jpg). Without the
underlayer, flowers, herbs and warmer variation return. Bare soil remains, because this forest-edge
habitat sits below the density at which the patch rule closes a meadow. Hidden cover shows the flat,
nearly uncluttered soil texture those gaps expose. These are diagnostic renders, not a candidate.

The original audit below describes the 18 October baseline; several identified
mechanical defects have since been repaired, with current evidence in DEVELOPMENT.md.

The owner reports poor performance, grass that looks partly buried, insufficient ground
coverage, and weak visual/color variety. This investigation finds concrete weaknesses in
the current implementation. It is an audit, not an implemented or validated repair.
DESIGN.md remains authoritative; the ground UI and gameplay are outside this change.

## What runs today

Yes, grass uses vertex shaders. `GrassField.cs:117` loads `surface_grass.gdshader`;
its `vertex()` positions and animates the procedural tufts using uploaded ground and
cover lattices. A separate, always-created authored-cover layer has its own vertex
shader, meshes and LODs (`GrassField.Authored.cs`, `surface_authored_cover.gdshader`).
Flat litter is a third workload using the procedural shader's other layer.

The saved AB Test B graphics file read during this audit selects **Low**, grass density
**0.6**, grass distance **0.7**, and render scale **0.75**. The running game's launch has
no quality override. These are saved preferences, not a readback of live shader uniforms;
they were not changed. At that distance the procedural grass starts shrinking at 15.4 m
and disappears at 29.4 m. These settings compound the coverage issue, but the structural
problems below also exist at higher presets.

## Performance: sparse pixels still incur substantial work

`GrassField.cs:114–116,160–179` creates square instance grids independent of grass density.
Each procedural tuft has 40 vertices / 30 triangles; litter has 24 / 18. Current Earth
has radius 1,200 m, giving level-12 cells with nominal edge length 0.460194 m.

| Preset | Tuft instances | Litter instances | Input vertices | Input triangles |
| --- | ---: | ---: | ---: | ---: |
| Low, distance 0.7 | 16,900 | 6,724 | 837,376 | 628,032 |
| High, distance 1.0 | 34,225 | 13,225 | 1,686,400 | 1,264,800 |
| Maximum, distance 1.4 | 66,564 | 25,921 | 3,284,664 | 2,463,498 |

These are source-derived submissions **per active cube face**, before shader rejection,
excluding authored cover and any repeated renderer passes. Up to three faces can be
active near a cube corner. They are not measured GPU invocation counts or frame times.

Both procedural meshes use 12 km-wide custom bounding boxes (`GrassField.cs:239,306`).
Whole batches can be culled, but their individual cells are not spatially culled.
Godot documents this all-or-none behavior for
[MultiMeshes](https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html).
Density, distance, face and card rejection happen inside the vertex shader, after
hashing, five lattice/cover fetches, a noise fetch and coordinate calculations
(`surface_grass.gdshader:95–168`). Rejected vertices collapse to a point. This saves
subsequent work and rasterization, but does not avoid submitting those instances.
Surviving vertices repeat per-tuft color, moisture and wind work for each vertex.

Authored cover adds up to 18,000 plants across 36 family/variant/LOD batches; some near
meshes have 2,782 triangles and middle meshes 2,121. Its CPU selection, transforms and
buffer uploads refresh after 2 m travel. Fine lattice generation is on a worker after
14 m travel, followed by main-thread texture uploads. These are plausible movement
spike sources, not measured current spikes. Shadow casting and GI participation are
already disabled for cover. `GrassField.Tufts` currently returns only the authored count,
so the logged `grass=` statistic omits the procedural grids entirely.

Historical `custom-legacy` ablation recorded GPU means of 14.33 ms at baseline start,
11.94 ms with the entire Grass parent hidden, and 16.03 ms after restoration
(`.artifacts/loading-performance/custom-legacy.stdout.log`). It establishes historical
cover cost, but mixes all three layers, has baseline drift, and predates current sources.
It is not today's exact grass cost. The later Maximum r4 route measured 24.36–29.63 ms
GPU means without isolating grass. No new GPU timing or optimization claim is made here.

## Why coverage is poor and grass looks buried

1. **Coverage is reduced repeatedly.** Canopy, substrate and slope already reduce the
   input (`WalkingWorld.cs:518`, `GroundDetailCover.cs:17–21`). The shader then removes
   whole tufts, reduces cards in survivors, narrows them and shortens them
   (`surface_grass.gdshader:128–161,213–214`). At a final shader density of 0.25,
   approximately 25% of cells survive with an average 2.06 of five cards and 46.5% of
   dense width. The count-times-width proxy is only about **4.8% of dense coverage**
   before height and alpha masks. This illustrative algebra is not measured ground area.
2. **The fade flattens blades.** At the midpoint of the 22–42 m shrink band, scaled by
   distance preference, blade height is 50% while horizontal footprint is still 75%
   (`surface_grass.gdshader:146,251–254`). This can look cut off even with correct roots.
3. **The authored crowns still have a slope-contact problem.** Their centers use
   bilinear interpolation rather than the terrain triangle diagonal, receive a 2.5 cm
   downward offset, and remain radially upright without root correction across their
   width (`GrassField.Authored.cs:111–132,172–184`; authored shader:31–37). Their uphill
   sides can intersect terrain. Read-only GLB inspection found the first two wild-grass
   variants only 15.7–17.7 cm tall and about 50–55 cm wide before scaling. A uniform
   downward offset and slope are significant for these shallow meshes.
4. **Independent silhouettes remove more grass.** Painted atlas alpha is intersected
   with an independently generated ragged-top and blade-strip mask, then discarded below
   0.5 (`surface_grass.gdshader:302–321`). That construction can sever upper blades and
   lose coverage under minification. The exact contribution needs intermediate-mask
   captures. Existing closeups already show angular forms and detached-looking tips.

The procedural layer now has triangle-plane root correction and only 5 mm burial
(`surface_grass.gdshader:108–113,284–285`). The earlier repair did not add equivalent
correction to authored crowns. It is inaccurate to say every blade is uniformly half
underground. Contact against actual morphed terrain and across creases remains unmeasured.
The current seed varying is already `flat`; the older interpolated-seed hypothesis is
not a current source defect.

## Why lighting and variety look flat

Grass is lit: both shaders consume light color, attenuation and cloud shadows, and
`SurfaceSky.cs:202–246` updates sun/moon color and intensity. The missing visual response
is not explained by an unshaded material.

- Procedural normals are constrained to within **6.84 degrees of planetary up** by the
  source normal and the shader's 2.5-times vertical bias (`GrassField.cs:271`, shader:288).
  They do not bend with wind. Individual blades therefore receive very similar diffuse
  lighting regardless of their shape or motion.
- Both custom `light()` functions add diffuse and transmission, but never
  `SPECULAR_LIGHT`. They provide no direct sun/torch specular lobe despite setting
  fragment roughness/specular values. Environment reflections can still exist.
  [Godot's custom-light contract](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/spatial_shader.html#light-built-ins)
  distinguishes those outputs; changing roughness alone will not supply that missing lobe.
- `blade_underlayer=true` disables the procedural flowers and non-grass plants
  (`GrassField.cs:122`, shader:137,203). Four authored families with three variants do
  exist, but the continuous field repeats one tuft topology. Species selection is
  conditional and some accents are rare.
- Authored tint is effectively brightness-only, while the procedural grass uses
  moisture/canopy hue shifts. Near terrain blends only 45% toward blade color; roots use
  the full blade palette plus a separate cool multiplier; authored plants retain scan
  colors (`surface_terrain.gdshader:204–211`, grass shader:293–296, authored shader:54).
  The terrain blend's hardcoded 22–42 m transition also ignores grass-distance preference.
  These layers do not share one coherent palette and distance transition.

## Evidence and next repair

The running game was inspected without gameplay input. Its capture at 13:53 Istanbul
was dark nighttime scenery and cannot establish daylight color, coverage or root contact.
That image was inspected in the tool output; the shared capture file was overwritten
before preservation, so no live screenshot is retained in this audit's evidence folder.
Source hashes are at `.artifacts/grass-investigation-20261018/evidence.json`.
No new game, editor, import, build or test was launched.

Original retained images inspected in this audit:

- `docs/images/repairs/p2-environment/baseline-max-day-r4/02-clearing.png`
- `docs/images/repairs/p2-environment/baseline-max-day-r4/01-woodland-roots.png`
- `.artifacts/p2-environment/grass-native-prep-r3/captures-r1/04-flat-roots.png`

The old route shows sparse, clipped-looking clumps. The small neutral fixture shows
angular silhouettes and fragmented-looking tips; its floor edges expose card bottoms
and must not be mistaken for a full-world root test. These images are historical,
not a validated rendering of all current source changes. There is no project
`.game-dev/adapter.json`, and this is not a sealed game-dev scenario result.

The proposed repair order is: measure the three layers separately at one fixed current
pose; correct both layers' terrain contact; establish one coverage policy with coherent
silhouettes and less flattening; then spatially partition/compact submitted instances
and move repeated per-tuft work out of vertex processing where beneficial. Coordinate
palette, bending normals and restrained direct specular/transmission across the layers.
Validate the same clearing and slope under midday, low sun and canopy shade, recording
GPU/CPU timings and restored baselines. Preserve quality, saves and ground UI. Increasing
density alone cannot address these causes. N6 visual quality and current grass performance
acceptance remain open.
