# Hollow Universe AB Test B

## 19 October 2026 — Defense battery interior sound installed

`defense.fire.interior` existed nowhere, so StationsView's own-hull shot played nothing and
logged `No sound event`. Installed P5's prepared recipe from `.artifacts/p5-defense-interior-prep-r1`
unchanged (it still fit the current `_iw_defense`): four files built by `art/audio/build.py
--only iron-wake/defense_interior`, imported by Godot, rebuilt as a no-op. The build re-sorted the
footstep events Codex had committed in another order; only the new event was inserted, so the
catalogue's diff is that one entry (a later full build will make the re-sort). New check
`SoundCatalogueTests.SoundCatalogueCoversBatteryFire`. Validation: red on the old catalogue,
`Sound` 8/8, `--sound-check` 0 problems. Python here can't open the worktree's long cache paths
(over 260 characters), so the build ran in a short-path detached worktree and its exact diff was
copied back. Listening and mix acceptance remain with the owner. Details in
[KNOWN_PROBLEMS.md](KNOWN_PROBLEMS.md#defense-battery-interior-sound-is-missing--17-october-2026).

## 19 October 2026 — T2 second iteration: sea-cliff toes, intended rock, the formation map

A coastal face's toe may stand in the surf (up to 40% of a piece's fit points over water, measured from a metre
below the water line), and coastal faces weight sandstone and nordic scans above the flat coastal slabs. Face
samples now say whether their face meant them to carry rock (`CliffFaceSample.Intended`), and the survey reports
how much of that ground is covered: coastal 79%, outcrop 89%, mountain 84%, plateau 77%. Steep ground with rock
75.6%, walls 73.5%, sea cliffs' walls 63.6%. `earth formations lat lon [size]` draws a site from above with each
scan's footprint and every face sample marked; at the escarpment it shows steps under about 6 m bare and unsurveyed.

The route planner keeps a walker's chest out of every scan piece's box (a walk of the 877 m route met T2's rock on
its cliff leg) and its water stop 20 m from the cliff stop; the route test checks both. A hosted High walk
completed the new 573 m route; one walk there averages 16.92 ms, in denser forest than the 868 m route.

Validation: solution 0 warnings, 0 errors; full suite 1,011 of 1,011 in 480 s. The changed collision is fenced
separately from the first iteration's: cliff generation 4, Earth save revision 7 (others 4), protocol 41.

## 19 October 2026 — T2 first iteration: cliff formations dressed along whole faces

`CliffPlacements` generation 3 surveys each 59 m cell on a 12 × 12 grid instead of testing one random point,
groups the steep samples into faces, and dresses each face as a whole from `Data/cliff-recipes.json` (coastal
scarp, plateau face, mountain face, inland outcrop): column after column from the highest sample still bare,
from below its crest down to its toe, until every intended sample lies inside a piece. Walls always take rock;
gentler ground keeps soil in coherent 30 m patches. `CliffPlacements.Report` returns every face sample with
whether rock covers it, and the survey sums it by recipe. Member offsets are measured across the ground, so very
steep slopes keep their formations. Earth: steep ground with rock 22.5% → 74.7%, walls 14.5% → 71.7%,
909 → 5,375 pieces. Earth save revision 6, other walking bodies 3, protocol 40.

Hosted High captures at the T1 sites: wall feet, outcrops and escarpment ledges are dressed; upper faces of tall
flanks, part of the 120 m wall and the sea cliffs' walls stay bare, coastal slabs read as shelves, and grey
scans stand on red tableland. Not yet T2's gate.

Validation: solution 0 warnings, 0 errors; full suite 1,011 of 1,011 in 485 s, including the new escarpment
and uniform-slope formation tests and the route's independent clearance check.

## 19 October 2026 — T0.4: measured baseline; the route keeps clear of solid objects

[ENVIRONMENT_BASELINE.md](ENVIRONMENT_BASELINE.md) records five breakdowns and four route walks at 1600 × 900
on the RTX 2070 SUPER. High: 16.0 ms a frame at the clearing, GPU-bound; ground cover 4.1–5.1 ms of GPU,
of which authored plants 1.6–2.9, procedural grass 1.6–2.2, litter about 0.1; trees 2.5–2.8; sun
shadows 2.4–2.8; cliffs, monuments and water almost nothing. The High route walks pool to 14.11 ms mean and
16.20 ms p95, the walks within 0.1 ms of each other; Low 5.87 ms. The client commits 10.4–11.7 GiB of
private memory at every preset. One walk completed the 868 m route; three stopped at a boulder inside a
waypoint. `EarthEnvironmentRoute.Find(world, natural)` now keeps waypoints 1 m and corridors 0.5 m clear of
every solid natural object, and the route test checks it independently.

## 19 October 2026 — T1: Earth landform survey, sites and the scalloped wall

[LANDFORM_SURVEY.md](LANDFORM_SURVEY.md) records T1 of the environment plan. `earth survey` classifies the whole
of Earth (5.24 m pixels) into coastal scarps, plateau or mountain faces, inland outcrops, wooded edges, meadows
and shores, measures which steep ground has scan rock on it, and names each class's largest regions with an
anchor and a viewpoint. `earth transect` and `earth wall` profile a face and draw it at each terrain level.

Steep ground is 0.711 km² of 10.668 km² of land; 22.5% of it lies inside a placed scan. Walls steeper than 2
are 0.103 km², 14.5% covered; tableland escarpments reach grade 16.7 and are 10 to 16% covered. The
playtest's regularly scalloped soil wall is the relief shape: a tableland escarpment rising 8.7 m in 3 m,
redrawn in one-cell stair-steps by terrain levels 5 and below, with grass and forest floor drawn top-down
and blended down the face from each cell's vertices. Lake rims, skirts and cliff placement were ruled out;
the scans are simply absent. Nine before-images were taken in hosted normal play at High, 1600 × 900 (the
tableland, mountain and coastal faces, an uncovered outcrop, the largest meadow, a wooded edge, a shore, the
largest lake's bank, and the wall from 120 m); the benchmark route is unchanged at 868 m.

Validation: generator report build 0 warnings, 0 errors; two survey runs give identical files; the route
planner test passes; eleven hosted runs exited 0 with at least 4.5 GiB free (two low wall views, hidden by
a wooded crest, are not used). No generation, save or rendering change.

## 19 October 2026 — T0.3: the Jolt crash was a native leak, now fixed

The native access violations in `JPH_MeshShapeSettings_CreateShape` and once `JPH_PhysicsSystem_Create`,
seen in four long runs on 16 and 18 October, were unchecked allocation failures. JoltPhysicsSharp 2.23.0
creates physics systems, shape settings and body-creation settings without ownership of their native
handles, so `Dispose` freed nothing. Every world and ship space leaked its system, bodies and shapes: a
standalone probe measured 5.6 MiB per empty world and 16.5 MiB with resident ground. The suite's process
grew about 16 MiB per test, to 9.4 GiB of private memory after 565 tests, while its managed heap stayed
near 100 MiB. A per-process commit cap reproduced the exact crash stack in the same beach-arrival test.

`JoltRuntime.HandOverToSystem` gives each system ownership of its handle and its three layer and filter
objects to it, because joltc's system destroy deletes them; every shape and body-creation settings
construction is now `.Owned()`. The probe stays flat across 120 worlds with ground and 120 ship spaces.
`PhysicsLifetimeTests.DisposedWorldsAndShipSpacesReturnTheirNativeMemory` measures it, and
`ArchitectureTests.JoltNativeObjectsAreReleasedWhenDisposed` checks all 28 constructions and both
physics-system files. The game leaked the same way: every terrain tile built by a server or client.

Validation: solution build 0 warnings, 0 errors. Full suite 1,009 of 1,009 in 464 s, against 638 to 677 s
before, with peak process commit 762 MiB against more than 9.4 GiB. The two intermittent loopback tests
passed in this run; memory pressure probably contributed to their timing failures.

## 19 October 2026 — environment plan resumed; grass contact on deep surfaces

The owner chose to continue the T0–T6 plan as designed. First, a T4 regression: since the drawn-contact
change, look-dev view (b), the loading picture's own command, threw "Drawn contact map supports terrain
levels zero through nine" on every frame (1,158 errors in one run) and never drew grass. Its full-size
planet's finest terrain level is 18. `GrassField` now checks addressability once, keeps heightfield
placement where the atlas cannot address the terrain, and logs the refusal once. Authored spatial cover
treats the same case as unsupported instead of throwing. Playable worlds use level six and are unchanged.

Validation: game build 0 warnings, 0 errors; the historical command reached ready in 15 s with no engine
errors at 2560 × 1421 on Maximum. The resulting then-and-now comparison and a landing-site diagnostic sheet
are in `docs/images/grass-regression/` and described in GRASS_INVESTIGATION.md. They are evidence for the
T4 visual target, not a grass candidate.

## 19 October 2026 — Codex stopped; recovery checkpoint

Codex's weekly limit ran out at 01:01 with most work since 16 October uncommitted and the client
build broken by a half-started flora integration. Claude reversed that integration's eleven
last-minute edits, kept the exact prior tree at `refs/backup/codex-stop-2026-10-19`, and committed
the rest as `74e51412` (admin self-camera) and `ba25d888` (everything else). Validation: solution
build 0 warnings and 0 errors; full suite 1,007 / 1,008, where `MultiplayerTests.TwoClientsSeeEachOther`
fails intermittently on timing and passes alone; a hosted Earth session on High reached play and saved a screenshot.
State, hazards and the plan are in [HANDOFF_CODEX_2026-10-19.md](HANDOFF_CODEX_2026-10-19.md).

## 19 October 2026 — Sealed Schematic original model and rendered icons

Inspected the official Rust Advanced Blueprint Fragment image before authoring.
Replaced the building-plan stand-in and SVG with an original complete blue
service drawing in a thin sealed sleeve. Curled edges, a heat-sealed perimeter,
fine grid and original cassette illustration preserve the reference's blue-paper
identity while following our existing item text. The sparse reverse is distinct.
No reference game pixels or meshes entered the repository.

ID 164, stack 20, description, loot and workbench requirements remain unchanged.
The model has three LODs (2,432 / 656 / 188 triangles), a 34-triangle convex hull,
one external 1024px PBR atlas and own 128/256px rendered icons. Opaque laminate
avoids transparent overlap. The existing diagram helper is unchanged.

Validation: all 12/12 focused catalogue/content checks and the full solution build
passed; review/test/solution builds reported zero warnings and errors. Native
Godot captures cover all LODs, the reverse and CatalogItemIcons at 32–256px with
no SVG fallback. Mesh attributes/unit normals and RGBA icon bounds passed.
Broader suite: 979/985 checks passed across all 170 classes. Six failures
in shared SelectedPhysicsGroundTests concern terrain-pin ownership, native
resource/completion expectations and a tile allowance. They do not load item art
and lie outside this change; they are recorded as current failures, not a blanket
suite pass. No unrelated physics code was changed.
Live gameplay inventory remains unchecked; artistic acceptance remains with the
owner. Jobs ran serially, below normal priority on two cores, behind CPU/RAM
gates. All owned processes exited and the reference tab closed. The commit
preserves other sessions' edits and staging.

See `docs/ITEM_ART_SEALED_SCHEMATIC.md` and
`docs/images/item-art-sealed-schematic-20261019/`.


## 19 October 2026 — Torn Schematic original model and rendered icons

Inspected the official Rust Basic Blueprint Fragment image before authoring.
Replaced the building-plan stand-in and SVG with original blue drafting paper:
a large torn corner, ragged outline, slight curl, fold and partial invented
service diagram. Front and reverse are distinct. No reference game pixels or
meshes entered the repository. Added a small original diagram-texture helper.

ID 163, stack 20, description and workbench requirements remain unchanged. Three
LODs, convex collision, one external 1024px PBR atlas and own 128/256px icons are
supplied. The final dimensions and triangle counts are recorded in provenance.

Validation: focused review/test builds passed with zero warnings/errors and all
11/11 catalogue/content tests passed. Native Godot captures cover all LODs, back
and CatalogItemIcons at 32–256px with no SVG fallback. Mesh attributes/normals and
RGBA icon bounds passed. The full solution build passed with zero warnings/errors. The broader isolated
suite did not start because free RAM fell to about 2 GiB, below its 3.5 GiB
launch gate. Live inventory remains unchecked. The initial build was deferred on 18 October; the resumed build corrected
Blender 5.2 tessellation index handling and passed with Python errors treated as
process failures. All jobs ran serially below normal priority on two cores. Owned children exited and
the reference tab closed; other sessions' edits/staging are preserved. Artistic
acceptance remains pending.

See `docs/ITEM_ART_TORN_SCHEMATIC.md` and `docs/images/item-art-torn-schematic-20261019/`.


## 18 October 2026 — Rifle Frame original model and rendered icons

Inspected the official Rust Rifle Body image before authoring. Replaced the
rifle-frame refined-metal stand-in and SVG fallback with an original dark stamped
housing, amber timber braces and ribbed steel covers. The shape, palette and
diagonal icon follow the reference; no reference game files or pixels were used.
The existing component mesh helper supplies simple cosmetic geometry. The asset
has no functional internals or working mechanism.

ID 162, stack 10, recipes, loot and recycling are unchanged. Corrected the old
stock description to match the stripped housing shown by the reference. Supplies
5,720 / 2,624 / 996 triangle LODs, one external 1024px PBR atlas, convex collision
and own 128/256px icons using catalogue view [-12,18,-38].

Validation: focused review/test builds passed with zero warnings/errors and all
10/10 catalogue/content tests passed. Native Godot captures cover all LODs, back
and CatalogItemIcons at 32–256px with no SVG fallback. Mesh attributes/normals and
RGBA icon bounds passed. Full solution and gameplay suite/live inventory were
deferred to keep load low. Jobs ran serially below normal priority on two cores
after CPU/RAM gates, waiting through a load spike. All owned children exited and
the reference tab closed; other sessions' edits/staging are preserved. Artistic
acceptance remains pending.

See `docs/ITEM_ART_RIFLE_FRAME.md` and `docs/images/item-art-rifle-frame-20261018/`.


## 18 October 2026 — Gun Receiver original model and rendered icons

Inspected the official Rust Semi Automatic Body image before authoring. Replaced
receiver's refined-metal stand-in and SVG fallback with an original rusty tubular
housing, pale timber braces, dark clips and tan bindings. The silhouette, palette
and diagonal icon view follow the reference. All geometry and procedural PBR
textures are original; no reference pixels or game files entered the repository.
Added reusable cosmetic component mesh helpers for atlas UVs and simple parts.

ID 161, stack 10 and gameplay records are unchanged. The description now matches
the timber-braced construction. Three LODs, one external 1024px material atlas,
convex collision and own 128/256px rendered PNGs are supplied. Native review
caught disconnected clip pieces; continuous wrap geometry replaces them.

Validation: focused review/test builds passed with zero warnings/errors, all 9/9
catalogue/content tests passed, including receiver identity. Native Godot captures
cover all LODs, reverse side and CatalogItemIcons at 32–256px without SVG fallback.
Mesh attributes/normals and RGBA icon bounds passed. Full solution and gameplay
suite/live inventory were deferred to keep load low. All jobs ran serially below
normal priority on two cores after CPU/RAM gates; owned children exited and the
reference tab closed. Other sessions' edits and staging are preserved. Artistic
acceptance remains pending.

See `docs/ITEM_ART_RECEIVER.md` and `docs/images/item-art-receiver-20261018/`.


## 18 October 2026 — Sign Plate original model and rendered icons

Inspected the official Rust Road Signs item image before authoring. Replaced the
sign-plate refined-metal stand-in and SVG fallback with an original three-plate
salvage bundle: red stop octagon, red/white CAUTION KEEP CLEAR panel and yellow
up-arrow. The arrangement and palette follow the reference, with original bends,
mounting holes, scratches and chipped paint. No reference pixels or game files
were imported. Lettering uses the repository's existing SIL OFL Barlow font.

ID 160, stack size 20 and recipes are unchanged. Updated the description to a
bundle of dented signs so it matches the model. The asset has 4,032 / 1,824 / 936
triangles, one material and a 1024px PBR atlas. It supplies its own 128/256px PNGs
through production IconRenderer with view [-22,14,-5]. Native preview review
corrected the warning's overlap, widened the arrow and exposed the STOP lettering.

Validation: focused review/test builds passed with zero warnings/errors; all 8/8
catalogue/content tests passed, including sign identity. Native Godot captures
cover three LODs, reverse side and CatalogItemIcons at 32–256px with no SVG fallback.
Mesh attributes/normals, RGBA transparency and icon bounds were checked. Full
solution and gameplay suite/live inventory were deferred to keep load low.
Jobs ran serially below normal priority on two cores with CPU/RAM gating; owned
processes exited and the reference tab was closed. Other sessions' edits and
staging were preserved. Artistic acceptance remains pending.

See `docs/ITEM_ART_SIGN_PLATE.md` and `docs/images/item-art-sign-plate-20261018/`.


## 18 October 2026 — Metal Blade original model and rendered icons

Inspected the official Rust item image before authoring, as requested by the owner.
Replaced metal-blade's refined-metal ingot stand-in and SVG fallback with an original
broad tapered grey steel plate: one mounting hole, actual bevels, small chips,
scratches and restrained oxidation. No reference pixels or other game files entered
the repository. ID 158, item description, quantities and recipes remain unchanged.

The model has 1,568 / 992 / 672 triangles, one material and an original 1024px PBR
atlas. The mounting hole and chipped silhouette remain in all LODs. Production
IconRenderer supplies 128px and 256px PNGs with per-item view [38,28,-15]. Larger
preview review caught regular bevel striping, softened into irregular abrasion
before final captures. The catalogue now selects this model and its own icons.

Validation: focused test and review builds passed with zero warnings/errors;
7/7 catalogue/content tests passed, including blade identity. Native Godot captures
cover LOD0/1/2, reverse side, and actual CatalogItemIcons at 32–256px without an SVG
fallback. GLB attributes/normals and PNG transparency/bounds were checked. Full
solution and gameplay tests/live inventory were deferred to keep resource use low.
Jobs ran serially at below-normal priority on two CPU cores, waiting for RAM/CPU;
owned children exited and the temporary reference tab was closed. Other sessions'
work and staging were preserved. Artistic acceptance remains pending.

See `docs/ITEM_ART_METAL_BLADE.md` and `docs/images/item-art-metal-blade-20261018/`.


## 18 October 2026 — Sewing Kit R02 after owner rejection

The owner rejected the green R01 sewing kit and its appearance. Reworked the original
asset around the observed Rust reference: warm reddish-brown fine thread, a narrow
worn hollow fibre core with nearly flush ends, a much thinner matching loop, and a
smaller flattened needle partially tucked under the winding. Removed the toy-like
rounded wooden caps and repeated chunky ribs. No reference pixels or meshes were used.

R02 has 7,200 / 2,748 / 1,060 triangles, one material, one original 1024px PBR atlas and
a 56-triangle hull. Both PNG sizes were re-rendered through production IconRenderer;
per-item view is now [20,18,0]. Description updated to match the fibre-core spool.
IDs, quantities, recipes and inventory behavior are unchanged.

Validation: the focused test project rebuilt with zero warnings/errors; 6/6 catalogue
and content tests passed. Godot imports, native LOD0/1/2 captures and 32–256px production
icon-provider checks passed with SVG fallback disabled. Geometry and alpha bounds
checked. The existing small review harness was reused, with serial two-core,
below-normal jobs and CPU/memory gating. All owned jobs exited. Full solution and
full gameplay suite/live inventory were not run to keep load low. Existing retained
R01 caches were reused; their previously rejected cleanup was not retried.

See `docs/ITEM_ART_SEWING_KIT.md` and `docs/images/item-art-sewing-kit-r02-20261018/`.
R01 evidence remains as rejected history; R02 artistic acceptance is pending.


## 18 October 2026 — original Sewing Kit model and inventory icons

Replaced Sewing Kit's fibre stand-in and flat SVG presentation with an original wooden
mending spool, sage thread, a steel needle with a modeled eye, and a loose thread loop.
Inspected Facepunch's actual Rust item image before authoring; links and observations
are in `docs/ITEM_ART_SEWING_KIT.md`. No reference game asset files were imported.
The item description now matches the spool. ID 156, stack size, recipes and behavior
are unchanged. The default SVG remains available as fallback; catalogue PNGs now win.

One original 512px PBR atlas/material; 7,200 / 2,892 / 944 triangles and a 54-triangle
convex hull. Repeatable source: `art/models/sewing_kit.py`; installed hashes:
`game/assets/items/sewing-kit.provenance.json`. Both transparent icon sizes were
rendered from the selected model using production IconRenderer.

Validation: isolated review and focused test builds passed with zero warnings/errors;
6/6 current catalogue/content tests passed. Native Godot imports and all three LOD
captures passed. CatalogItemIcons with SVG fallback disabled loaded 32–256px icons;
PNG transparency and bounds checked. Screenshots and measured receipts are under
`docs/images/item-art-sewing-kit-20261018/`. A UV atlas orientation error was caught
and fixed before final export; the first scratch test build needed the existing
friend-assembly name (`Hollow.Tests`) to compile internal content-reader tests.

Resource use: serial below-normal jobs on two CPU cores, with CPU/memory gating;
Blender, Godot and test children exited after each bounded job. The reference tab was
closed. Full solution rebuild/full gameplay suite and live gameplay inventory were
not run in this art task; the native icon diagnostic uses the real production providers
in an isolated project with copied compiled game assemblies. Owner art acceptance
remains pending. Automatic approval review rejected removal of this task's temporary
review caches as "blocked by policy"; caches were retained without a workaround.


## Cloth item art replacement — 18 October 2026

Implemented the first item only, following the owner's request to work serially at
low machine load. The owner then confirmed Rust as the main visual inspiration and
asked for actual item-image references. Inspected Facepunch's cloth image online and
recorded its URLs and visual observations in `ITEM_ART_CLOTH.md`; the plan now requires
that reference step for each item. No Rust image or model files were imported.

Cloth now selects an original loose folded rag with a procedural 512 px weave set,
three detail levels (5,916 / 2,356 / 968 triangles), a convex proxy and dedicated
128/256 px transparent PNGs rendered from that model. Removed its fibre stand-in
mapping. Item identity, recipes, quantities, inventory layout and lootbag behavior
are unchanged. Added repeatable Blender authoring, installed-file hashes, a regression
check and native close/distant model screenshots plus a diagnostic icon-size sheet.

Validation: original Blender export and GLB structure/finite-normal checks passed;
Godot 4.7.2 model/material/texture import, LOD0/LOD2 captures and production icon
rendering completed without engine errors. The small isolated C# review project built
with zero warnings/errors. Both catalogue tests passed (163 models, 171 model files,
252 icons), including the new cloth identity check, using a copied newer existing test
build. Both PNG sizes have transparent, unclipped edges. No full solution rebuild or
full suite was launched by this task while the shared computer was under heavy load.

Limit: live inventory/provider validation remains deferred. Two icon-import attempts
were stopped by the memory guard as free RAM fell; later idle waiters were closed.
The icon-size sheet is a Pillow composition, not a live inventory screenshot. The
runtime review used a cloth-only project and snapshotted production renderer binaries;
no world, server or player save was loaded. Owner visual acceptance remains pending.
Owned processes and the reference tab are closed. Automatic approval review blocked
recursive deletion of this task's temporary review caches; those files remain.
Editable source, logs and evidence are retained. See `ITEM_ART_CLOTH.md` and
`images/item-art-cloth-20261018/validation.json` for scope and receipts.


## Audio and visual-effects architecture audit — 18 October 2026

Audited the current simulation/contact, audio, footstep, projectile-impact and VFX paths
at the owner's request. The shared directors, pools, GPU batches and footstep/attack
foundations are worth retaining. The principal gap is material/contact information lost
before presentation, followed by client-side object/material guesses; current compound
stone structures can select wood responses and animal impacts fall through to terrain.
Continuous audio-loop recovery and VFX-loop budget enforcement also need consolidation.
The [audit report](reports/audio-vfx-architecture-2026-10-18.md) records source references,
evidence limits and a proposed repair order. No gameplay code or assets changed in this audit.

Fresh solution build passed with **0 warnings/errors**. Focused sound/FX catalogue,
footwear, movement/audio, dry-fire and attack lifecycle checks passed **44/46 in 24.6 s**.
The two failures are the existing missing large-furnace fire effect and missing item sound
bindings for compound/defense/fitting content. Logs are
`.artifacts/audio-vfx-audit-build-20261018.log` and
`.artifacts/audio-vfx-audit-tests-20261018.log`. No new runtime profile, listening/visual
acceptance or full-suite pass is claimed. Proposed architecture work remains unimplemented.

## Electricity and device authorization implementation — 18 October 2026

The owner authorized implementing the foundation and migrating windmill, Auto Turret and
SAM. These are still the only wired production items; only the two defenses use automatic
friend/foe targeting. Batteries/solar/branches remain later content.

Implemented stable data-defined physical ports, durable directed cables with decorative
support-local anchors, indexed graph/component invalidation and transfer handlers for
generation, residual consumers and storage. Static circuits do no idle work; wind/storage
circuits update on the existing one-second utility cadence. A local edit test across 1,000
unrelated circuits verifies that rebuild/evaluation work remains local (not a wall-clock
server-load benchmark). Disabled or undersupplied consumers pass available power onward.

Added the Wire Tool through the common item/held/input registries, crafting data, an original
primitive model and explicit editable held profile. Output → optional surface anchors →
input uses a server draft, 20 m **physical endpoint** limit, occupancy/cycle guards, current
rights at both ends, finite bounded payloads, actual port cube bounds, click reach and
registered target geometry for obstruction. Right-click cancels; R disconnects. Intervening
deployables are now checked in addition to ordinary building/terrain sight. Ports are shared
cube meshes; cable geometry is retained until its route or endpoint/support poses change.
The old radial wiring verbs remain append-only tombstones and cannot bypass the tool.

Shared device authorization persists grants, exclusions and FollowCupboard/ManualOnly.
Clear-self denies inherited protection; clear-all persists ManualOnly; self-add never
silently restores inheritance. Managers remain able to configure after clearing their
friendship. Overlaps currently require all covering TC memberships, matching building
privilege; this is an engineering default, not a new owner-approved overlap rule. New
unclaimed devices grant the placer removable local protection. SAM uses the pilot's identity
or authoritative owner when unpiloted; passengers and reciprocal crew lists do not confer
immunity. Hold-E options use viewer-specific access facts. Power/operation switches are unified.

Protocol 36 and thing codec 10 carry explicit ports/routes, viewer access facts and durable
authorization. Routes are sent on change and retained across unrelated state deltas. Static
world-position fallbacks keep anchors/endpoints drawable across object relevance boundaries.
Legacy incompatible fan-out, span or corrupt links are disconnected with startup diagnostics;
legacy defenses use FollowCupboard without importing hidden owner/team immunity. Their enabled
state is old power AND defense. Restore validates after all modules restore their objects.

Validation checkpoint: solution builds with zero warnings/errors; 41 focused content,
power, wiring, authorization, defense, persistence, two-client/restart and held-presentation
checks pass. Native Godot review renders use production models/port definitions/cable renderer;
screenshots are in `docs/images/electricity` (overview and port detail, inspected). The
compatibility renderer emitted existing graphics-setting warnings for unsupported AA/FSR;
the review completed successfully. This does not claim a manual multiplayer input capture.

The full engine-free suite completed **557/566 in 627.6 s**. Two new-item presentation
failures were found (missing admire curve and primitive scene not meeting the mesh catalogue
contract) and corrected separately. The cannon packet-loss and held-use timing failures both
passed isolated reruns; the station trace showed late/stale attack expiry under packet loss,
with no electrical devices in that fixture. Five failures reproduce independently in areas
outside the changed electricity/auth rules: `EffectsCatalogueTests.EveryItemNamesItsEffects`
(large furnace fire binding), `SoundCatalogueTests.SoundCatalogueCoversItems` (existing
compound items/fittings lack audio bindings), `EnvironmentDetailTests.EarthMonumentsSharePadsClearingsCollisionAndAuthoritativeLoot`,
`EnvironmentDetailTests.EarthCliffsHaveStablePlacementsAndSharedCollision`, and
`GeologyPortTests.CliffChainsHaveDistinctStablePartsAndStreamAcrossCellEdges` (independent
environment generation comparisons). No blanket full-suite pass is claimed. Raw logs:
`.artifacts/electricity-build.log`, `electricity-focused.log`, `electricity-full-suite.log`,
and `electricity-isolated-failures.log` in the same directory.

Final checks: **43/43 focused tests pass in 10.8 s**, now including the model catalogue and
all held/admire presentation coverage; `.artifacts/electricity-final-tests.log`. Final solution
build: **0 warnings, 0 errors**. The Wire Tool is an original primitive source baked by
`WireToolAssetBuilder` into embedded ArrayMesh LODs, external PBR materials, grip/tip sockets
and a convex collision wrapper; regenerate with Godot `-- --build-wire-tool`. Generated
128/256 icons and the native first-person screenshot were inspected. The character review
exits successfully; its compatibility-renderer warnings include unsupported skin effects
and an existing ObjectDB instance warning on exit. This is functional foundation art, not
a polished final tool model. The compound gameplay tour now uses actual Wire Tool intents
for windmill → SAM → turret; the full-world tour was not run during this bounded validation.

Known scope limits: static ground wiring only; no independently streamed distant route-only
segments; no automatic migration backup/receipt file; no full populated-server performance
benchmark. Claim geometry invalidation still uses a global generation and lazy per-device
coverage refresh rather than a spatial dependency index. The native review is a presentation
fixture, not evidence of a complete manual multiplayer playthrough. Existing unrelated staged
and unstaged work and running user processes are preserved; no mixed commit is created.

## Electricity and device authorization architecture — 18 October 2026

The owner requested shared architecture/general logic before further item-specific work.
The source/data audit identifies exactly three wired production deployables: windmill,
Auto Turret and SAM site. Only turret/SAM currently make automated friend/foe decisions.
Solar panels and batteries are design commitments without production items; generic
storage logic/tests exist. Cupboards, locks/compound gates and ship crew/controls are
related access systems. Furnaces burn fuel and cannon batteries are not energy storage.

DESIGN.md ELEC-02–03 and AUTH-01 now record physical cube ports, the Wire Tool, decorative
route anchors, one output per generator, one input/output per battery and one input/
passthrough per turret/SAM. The owner explicitly chose **20 m between endpoint ports**,
not routed length, and **clearing inherited TC protection too**. Self-clear must override
TC protection on that device; clear-all must stop its inheritance without clearing the TC.

[ELECTRICITY_AND_AUTHORIZATION.md](ELECTRICITY_AND_AUTHORIZATION.md) records the proposed
ownership boundaries, directed flow, indexed/event-driven scheduling, atomic wiring,
local grants/exclusions plus persistent inheritance mode, separate management permissions,
claim/aircraft identity choices, relevant replication, migration and acceptance scenarios.
It identifies current gaps: eight-output/30 m windmill data, an undirected whole-component
power bus, source-entity-only links, all-world scans, no physical routing/port identity,
and friend status derived from management/owner/team/crew shortcuts. Existing authored
input/output sockets and the common weapon/interaction/held pipelines remain reusable.

Disabled/undersupplied passthrough, enrollment/control permissions, overlapping TC
association, mixed-crew SAM rules and legacy-link migration details are explicitly
proposals. Architecture review covered conservation, simultaneous wire commits,
authorization invalidation before firing and persistence of clear-all across restart.

This is **documentation-only architecture work**, not a playable implementation. No source,
content, assets, saves or running game processes were changed. Validation is limited to
source/data inspection, independent read-only reviews and scoped document/link/whitespace
checks. No build, engine-free suite, Godot capture or performance result is claimed. The
implementation sequence requires those checks when the corresponding code/visual work lands.
The existing mixed staged/unstaged checkout was preserved; this pass is not a mixed commit.

## Centered configurable ground crosshair — 18 October 2026

The owner requested crosshair customization in Settings, one marker instead of the separate HUD dot and firearm
spread ticks, an exactly centred position, and a dot as the default. DESIGN.md UI-06 records that requirement.
`CrosshairPainter` now draws both the ground HUD marker and the settings preview from geometric primitives at the
actual viewport midpoint. The old Barlow middle-dot text at `(789, 428)` and `GroundOverlay`'s independent ticks
are removed; reload progress remains in that overlay. `CrosshairView` carries only presentation state from the
ground camera/weapon adapter. Simulation, gun handling, ballistic spread and generated content are unchanged.

The CROSSHAIR tab offers Off, Dot, Cross, Cross + Dot, Ring and Ring + Dot; white, green, cyan, yellow, red and
magenta; size, dot diameter, line thickness, gap, opacity and outline. Its preview uses the same painter at actual
HUD size. Inapplicable controls are dimmed and disabled. Optional dynamic spread expands crosses/rings only while
hip firing. Show While Aiming defaults on, preserving the former HUD dot's visibility; players can switch it off
for ADS. The default is a static white dot with a dark outline. Inventory, menus, radials, death, helm, weapon
station and admin-camera contexts suppress the ground marker; physical optics and dedicated ship sights remain
separate. Preferences use `[crosshair]` in the existing isolated `user://graphics.cfg`, including existing
Apply/Revert/Defaults handling; changing graphics presets preserves them, and older files get the dot default.

Validation: `dotnet build HollowUniverse.sln --no-restore` succeeds with zero warnings/errors. The native Godot
`--view ui --crosshair-check` passes **53/53** checks: actual rendered pixels for every visible shape at 1280×720
and 1281×721, UI scales 0.8125 and 1.25, measured centre error 0 px, symmetry and distinct components, ADS/hidden/menu
visibility, dynamic spread and FOV, in-memory configuration round trips and invalid values, graphics-preset
independence, keyboard changes, inactive controls, and reverting on exit. Component checks use separated arms
so antialiased outlines do not join through the centre gap.

The UI viewer uses a sample backdrop; this is native HUD/settings rendering evidence, not a multiplayer weapon
playtest. [Settings capture](images/crosshair/settings-crosshair.png),
[centred dot](images/crosshair/1280x720-scale0.8125-dot.png) and the other captures are in `docs/images/crosshair/`.
The full engine-free run is **not green**: 485 tests passed and seven failed before the process aborted during
`SurvivalNetTests.OthersSeeOnlyABag` with native Jolt error `0xC0000005` in
`JPH_MeshShapeSettings_CreateShape` / `PhysicsWorld.BuildShape`. The remaining tests did not report results.
The seven failures are in untouched content/environment/ship paths:

- `EffectsCatalogueTests.EveryItemNamesItsEffects`: large-furnace lacks its fire effect.
- `EnvironmentDetailTests.EarthMonumentsSharePadsClearingsCollisionAndAuthoritativeLoot` and
  `EarthCliffsHaveStablePlacementsAndSharedCollision`: independent generated sites/placements disagree.
- `GeologyPortTests.CliffChainsHaveDistinctStablePartsAndStreamAcrossCellEdges`: generated cliff members disagree.
- `ShipAttitudeNetTests.ShipAttitudeTwoClientsObserveHoldAndActualHelmTakeover` and
  `ShipPartsTests.TwoSeatedInAFlyingShip`: one prediction correction where the tests expect zero.
- `SoundCatalogueTests.SoundCatalogueCoversItems`: several existing compound/deployable items lack sound mappings.

No simulation/content fixes were folded into this UI task. Logs are `.artifacts/crosshair-build.log`,
`.artifacts/crosshair-native.log`, `.artifacts/crosshair-engine-tests.log` and the settings-shot log alongside them.
The native crosshair/settings runs emitted no errors. The user's existing game process was left running, and
these UI checks did not save or overwrite user preferences or load a world save. Restart that game to load the
new assembly and Settings tab.

## Elite Dangerous-inspired travel design revision — 18 October 2026

The owner requested a documentation change to Elite Dangerous-inspired space travel, explicitly including frame
shift drive charging and fuel scooping. DESIGN.md TRAVEL-01–05 now specifies normal flight, supercruise within a
system, charged interstellar jumps, fuel/range constraints and scooping. This supersedes the former direct-flight/
no-supercruise requirement. Continuous planetary ascent/descent, the 1,000-star limit, 23.04 km² planet surface cap,
Earth start, and ground UI remain the current requirements. Map presentation still follows GAL-02.

Drive timing, distance/speed balance, fuel type, scoop equipment/rates/star eligibility and detailed heat, mass-lock,
alignment, safe-drop and escape rules remain open or explicitly proposed. The reference term FSD does not settle the
final in-universe equipment name. The charging/arrival/scooping interface uses original art/audio. Frontier's linked
Player's Guide informs the three-mode/charging reference; it does not automatically approve its entire feature set.

AGENTS.md and the current scale/local-flight notes now follow this direction. TRAVEL_IMPLEMENTATION.md retains
the required multiplayer ownership, ship/crew/cargo transfer, prediction, persistence and streaming work, and adds
authoritative travel states, shared fuel accounting, charge/cancel/commit, scooping and two explicit validation
milestones: a planetary return journey through supercruise, then an interstellar jump/scoop/return journey. Older
design, space-concept and scale documents carry precedence notices; their unrelated decisions remain historical.

This is a **documentation-only revision**. Galaxy generation/maps and local orbital-altitude flight exist; connected
space, drive charging, supercruise, hyperspace jumps and fuel scooping remain unimplemented. The current outer local
flight boundary still blocks outward movement and shows "LOCAL FLIGHT LIMIT · TURN OR DESCEND". No source code,
assets, generated content, saves or runtime behaviour changed in this revision. Document consistency review,
local link/anchor checks and whitespace checks passed. No build, engine-free tests or Godot run is claimed for
these prose edits; the existing live game/server were left running.

## Resources, fitted building parts and compound utilities — 17 October 2026

The owner requested resource nodes first, five window fittings, two missing double doors, Compound Utilities
R02 with working utilities, broad varied Earth terrain, smoother on-foot globe dragging and readable grass.
Verification is focused on the changed paths.

Missing nodes were a renderer ownership error: scatter entries were claimed by vegetation even when its
flora library had no geometry. Ownership now requires actual variants; stage-model rendering waits for
the registry. The first native run rendered a generated stone node, mined every stage with normal attacks
and hotspots, delivered exactly 294 stone and removed the depleted model. The driver then incorrectly
expected a private server respawn deadline in client state. That assertion was removed; respawn gameplay
was unchanged. Final native integration results will be recorded below.

Five window assemblies reuse door/aperture placement, damage, repair, pickup and persistence. Fixed
fittings cannot open or take locks; convex parts preserve firing holes and glass blocks shots. Wooden and
armoured double doors now have items, recipes and progression. One fitting occupies each aperture.
Existing door indices are preserved. Horizontal/vertical embrasures use items 237/236 because existing
salvaged axe 221 and alternate lamp-oil recipe 220 reserve those IDs. New recipe IDs are 300–318.

All 228 source entries of owner-authored PSG Compound Utilities R02 were SHA-256 verified. The importer
preserves notices, geometry, PBR textures and named articulation, with 12 assemblies and 90 convex hulls.
Manifest design coordinates precede the delivery's export rotation; their conversion deliberately differs
from measured dimensions. `game/assets/compound/r02/integration.json` records the admission. This legacy
delivery is not represented as a canonical game-dev package. Extracted original textures and material
remaps allow automatic mesh LODs and GPU texture compression; GLB payloads total 66.6 MiB.

Engine-free systems implement wiring, wind generation, optional battery storage, installed-firearm
defenses, ballistic SAM targeting, gates, climbing, barricade contact damage, large-furnace cooking and
deployable repair. Defense and electricity switches are separate. SAM aim predicts flight; missiles do
not home. Compound placement excludes ships. Existing ground inventory/crafting/interaction flows are
retained. Balance is provisional. Protocol 35 carries optional utility state and dynamic lock capabilities;
thing codec 9 preserves wiring, switches, charge, gate state, repair delay and key/code locks while decoding
older records. Compound gates reuse the structural doors' shared lock rules, serialization and placement;
locks follow the moving leaf and prevent pickup. Ladders retain compound-wall support across reloads.

Earth layout 5 was selected from six equal-profile candidates. It has 10.62 km² dry ground, 6.19 km² open/gentle
terrain proxy and 99.99% connected dry ground within 18.10 km² total surface. Suitability is not a guarantee
that placement succeeds. Revision 4 selects a new default save filename and leaves previous saves intact.
The deliberately changed golden is `0x3FD0EDEC0D0C72A5` (10 lakes, 532 drainage pieces).

Grass restores the earlier 0.28–0.54 m blade range and readable width/opacity, retaining triangle-plane roots,
wind bending and exclusions. Moisture, tree cover and patch variation affect colour and height. Globe
north-settling no longer competes with held dragging; terrain revision participates in cache identity.

The integrated solution build `compound-build-final1` passed with 0 warnings/errors. Focused checks initially
passed 34/35 in 10.8 seconds; the sole failure was a test expecting zero instead of the established
`long.MinValue` never-damaged sentinel when reading an older save. After correcting that assertion, the
test project rebuilt cleanly and that test plus four existing door/lock regressions passed 5/5 in 1.7 seconds.
All 39 distinct selected checks pass across those runs. The new Earth golden, two-client lock updates,
placement, repair, power, defense, maps and content checks are covered; no full-suite pass is claimed.

All [19 item icons](images/content-integration/added-icons.png) were rendered and inspected in Godot at
1280×720. All 13 new destruction-debris sets generated and imported. The earlier native resource run
confirmed real stone-node geometry, mining stages, hotspots, exact yield and depletion; its grass is visibly
above the ground with varied colour. Later full-world resource/map/fittings/compound tours could not complete:
world loading reached the physical-memory guard (last attempt 1,501 MiB available). Those attempts are not
validation passes. Explicit `--windowed` now takes priority over saved display preferences without changing
those preferences. The remaining scripted tours are available, but no more capture retries were made in
keeping with the owner's request to prioritize completion. Other ore families have the same corrected
render ownership and matching stage assets; their full native mining runs remain unverified. Map dragging's
camera-conflict regression passes, but this session has no completed native drag timing comparison.

Use the normal crafting/progression screens for the added items. With E held, select the windmill's power
output, then connect a consumer's input. Turret storage accepts an installed firearm and matching ammunition;
the SAM accepts HV rockets. The large furnace uses normal fuel/input/output cooking slots. Gates accept the
existing key/code locks, and ladders mount on vertical structural or compound walls. Default Earth saves now
use revision 4; old saves are preserved rather than placed on the new terrain.

Build, focused-check and capture logs are under `.artifacts/content-integration/`. This work was not committed
as a mixed checkout snapshot: the repository also contains unrelated concurrent character, weapon and ship work.

## Frontier AK recoil, optics and manual-station aim — 17 October 2026

The owner's recoil report reproduced two presentation discontinuities: automatic
fire discarded the previous shot's recovery (5.50 mm / 0.904° instantaneous reset),
and the hip reach solver jumped by 164.61 mm in one 120 Hz step. Procedural fire now
adds complete shot pulses, gun reach uses the nearest feasible correction, and
idle breathing continues through ADS and firing. The AK's aimed visual recoil is
scaled consistently for irons, holo and scope; gameplay recoil/spread are separate.

First-person reticles follow screen centre and remain clipped by the actual lens.
The 4× image uses a camera-aligned screen-space crop, preserving its registration
while the physical scope moves with the gun. Zoom no longer divides angular mouse
sensitivity. Manual stations converge their physical muzzles on the camera-centre
target, retaining mount limits, slew, obstruction, gravity and inherited velocity.

The holo's rear emitter covered the centred dot late in the first candidate burst.
Lowering that assembly by 18 mm clears the opening without moving the eye, lens or
gun. Its 966 LOD0 triangles and lens vertex data are unchanged. All 361 sampled
aperture rays now clear the housing, compared with 196 before.

The final shared solution build and Godot import pass with zero build warnings or
errors. Native tests on the actual shared project pass for male at 1280×720 and
female at 960×720: 2,832 sampled poses, 96 real fire triggers and 48 rendered frames
plus 12 optical-camera images. Trigger displacement is zero; maximum frame movement
is 7.81 mm at 120 Hz (2.66° hip / 0.48° ADS). Hand attachment, optic rigidity, scope
camera projection, rendered target registration, centred reticles and recovery all
pass. Both contact sheets were inspected. The owner subsequently
rejected the rifle's support-hand pose in these captures: it must approach from
below-left with the inner palm toward the fore-end.

The support-hand correction is now applied to the rifle profile alone. Its palm
faces the lower-left fore-end, the thumb lies along the near side and the fingers
cup its width rather than closing through the wood. No wrist offset is used.
Static bone geometry informed the pose, then full-resolution opposite-side and
underside captures were inspected. The reload was sampled through extraction,
carrying, insertion, charging and return; magazine attachment is retained. This
does not constitute a new authored reload animation or skin-intersection proof.
Final male and female burst runs repeat all 2,832 poses, 96 triggers and 48 renders
with zero violations; the maximum frame/trigger values above remain unchanged.
Final grip and optic evidence is in `docs/images/recoil-repair/grip-final/`.
The shared catalog was checked against the pre-grip copy: only the rifle's left
hand differs. Earlier grip candidate folders are diagnostic history.

The station network check exposed near-target oscillation: tracking from the
articulated muzzle fed the mount's own motion back into its target-following rate.
Tracking now uses the pitch pivot with authored transverse offsets retained.
Physical muzzle launch points, convergence and obstruction checks are unchanged.
A 240-step regression covers 20 m, 30 m and 2 km targets in two hull orientations.
Final solution build passes with zero warnings/errors; all 60 focused combat,
station, projectile and presentation-timing checks pass in 141.2 seconds.
The latency/loss check confirms exactly two predicted and authoritative shots.
One earlier lossy run hit request-deadline/stale refusals; a fresh single-test rerun
and the final grouped run passed without transport-rule changes. This timing
intermittence is retained in the logs, rather than presented as continuous-network proof.

Recoil checkpoint images and per-frame reports, before the support-hand correction:
[male](images/recoil-repair/final-male/recoil-contact-sheet.png) and
[female](images/recoil-repair/final-female/recoil-contact-sheet.png).
Guarded receipts, the reproduced baseline and asset hashes are under
`.artifacts/recoil-repair/`. The early isolated fixture used HEAD content during
concurrent integration; final evidence uses the current full project. Interrupted
guarded runs are retained and are not counted as passes.

After the asset coordinator released the runtime, the small held-editor fixture
used a 4 GiB start gate based on measured runs, retaining the 2 GiB / 85% commit
runtime stops and exclusive process checks. These captures used at least 3.84 GiB
available memory in the final male/female passes; no full-world scene was launched.

The fixture checks rendering and pose continuity, not subjective animation quality
or player acceptance. Godot still reports one ObjectDB instance at shutdown, also
present in the baseline. Moving-ship aiming under latency retains the existing
rendered-client/current-authority hull-pose policy; the new geometric tests do not
establish exact visual agreement for that case.

## Held-item authoring — 15 October 2026

The owner requested a complete held-item FPS/TPS presentation repair and a visual editor.
The implementation contract and user workflow are in [HELD_ITEMS.md](HELD_ITEMS.md).
The baseline working tree built with zero warnings/errors before this work. It includes
uncommitted changes from other repair packages; those changes are being preserved.
Shared presentation profiles now cover all 41 held models and 175 actions. The standalone
`Edit-Held-Items.cmd` workshop edits FPS/TPS placement, hand grips, fingers and action keys,
using the actual game renderers. It supports timeline scrubbing, grip dragging, undo/redo,
validated atomic save/reload, and simulation-timed preview. Separate FPS arms and the TPS body
reuse the existing character assets; this does not add new mocap or a dedicated arms mesh.

Melee presentation follows the simulation's strike tick; accepted hit markers identify the
weapon, swing and actual contact. Misses no longer play an impact reaction. Modeled use/throw/
repair actions have a cancellable wind-up, consumed last-stack items retain their recovery,
and observers receive same-item slot cancellations. The supplied Rust bow video informed
the low carry, raised/canted draw, string-hand movement and release. No reference-game assets
were imported.

Shared build 13 passed without warnings/errors. The integrated engine-free suite passed
376/376; both body variants passed 175 actions / 2,762 sampled FPS/TPS poses each, including
actual editor control/history/save checks and a 25 mm holding-hand attachment gate.
Actual screenshots and the bow recording are in `docs/images/held-items/`.
The rifle's actual magazine/bolt are now separate parts, preserving geometry, UVs, materials,
eight sockets and six hulls. Final gun checks passed 12 actions / 200 FPS/TPS samples for each
body variant. All 56 frames of the rifle reload were inspected after correcting early and late
camera clipping; the real magazine leaves and returns to the gun with the support hand.
The exact evidence and existing ObjectDB/Jolt shutdown warnings are recorded in
[HELD_ITEMS.md](HELD_ITEMS.md). The combined repair session owns the integration commit.

Godot 4.7.2 .NET / C# was selected with the owner on 15 October 2026.
`DESIGN.md` is the authoritative design snapshot. The original Hollow Universe is the source of
reused code and assets; this checkout and its user data are independent.

## Run

Double-click `Play.cmd` for the main menu. It builds the client and server and checks asset imports.
From PowerShell: `./Play.ps1 -Mode Host -PlayerName Ada` or
`./Play.ps1 -Mode Join -Address 127.0.0.1 -PlayerName Bea`.
These launcher sessions use port 27025 to avoid the original project's usual port.
Join addresses accept IPv4 or host names; the current client has IPv6 disabled.
The existing `play-ground.cmd` and development viewers remain available.

## Current implementation wave

| Work | Owner | Status |
| --- | --- | --- |
| Independent copy, save/settings isolation, build and launcher | Integration | Implemented; client and release server build cleanly |
| Earth starting world and 23.04 km² whole-surface cap | World generation | Implemented; hosted Earth radius 1,200 m, area 18.10 km² |
| Finite playable galaxy, at most 1,000 stars including companions | World generation | Implemented; default 1,000 stars in 610 systems, rendered map checked |
| Separate Vehicle/Engineering Workbenches and spacecraft unlocks | Survival/progression | Implemented; progression and network authority checks pass |
| Ground menu startup and sensible graphics defaults | Client quality | Implemented; menu and hosted gameplay captured and inspected |
| Continuous ascent/descent through local orbital altitude | Flight | Implemented inside one world; actual Earth scale tested |
| Normal-space connection, supercruise, charged jumps and fuel scooping | Travel | Design revised 18 October 2026; implementation and end-to-end validation pending |

## Quality gates and remaining development

The inherited game is a foundation, not a finished implementation of this design. Compilation,
engine-free tests, multiplayer checks and actual rendered captures establish each milestone.
The first connected milestone targets gathering, crafting, building, ship boarding, flight,
two-player interaction, and saving/rejoining. Interplanetary travel must be tested as real continuous
gameplay before being reported complete.

Further work packages include playable system/route layout, normal-space flight, supercruise, drive charging,
interstellar jumps and fuel scooping; server handover between worlds;
planetary streaming; surface/space visual transitions; modular ship upgrades; ship capture;
planet-size-dependent monument placement and loot; weather; shared oxygen equipment; electricity; turret authorization;
autominers; research/rare variants; respawn points across worlds; fauna; sound and impact feedback;
save recovery; multiplayer security; load testing; and visual polish. Proposed solo activities in
PLAY-03 remain proposals until selected. Each package must integrate with shared systems and be
validated before adding its full content catalogue.

## Coordinate decision

The revised travel model does not require a custom precision engine build. Existing physics uses planet-local and
ship-local positions; keep ordinary Godot and preserve that separation. System positions and interstellar route
metadata use double precision in plain C#, with local rendering and collision near the player. Map diagram units
remain separate from flight distances. Normal flight, supercruise and jumps are explicit player-facing travel modes;
the coordinate transforms and runtime handovers behind them are implementation details. Distance/speed tuning remains
open rather than assuming the earlier direct-flight compression policy.

## Workbench progression

CRAFT-01 through CRAFT-05 now use the existing shared interaction, crafting, and blueprint systems.
The Vehicle Workbench has a separate, unlevelled tree containing the Tin Finch. Learning the ship costs
salvage; crafting it still requires a Vehicle Workbench. A standard level-3 workbench cannot substitute
for the vehicle station, and its speed bonus does not accelerate specialized assembly. The tech-tree
screen keeps its existing layout and opens the particular bench used, including when several different
benches are nearby. The server validates the actual interaction target before charging salvage.

The Engineering Workbench is craftable and placeable, uses its existing owned mesh and icons, and has
its own empty tree. Its technologies remain undefined in the design; no engineering catalogue has been
invented. Both specialized benches remain level zero. The three standard workbench trees remain available.
Existing item IDs and keys remain stable; Engineering Workbench adds ID 172. Display names now match
Stone Axe, Stone Pickaxe, Research Bench, and Vehicle Workbench in the design.

Provisional balance, subject to playtesting: the Tin Finch unlock costs 500 salvage; an Engineering
Workbench takes 45 seconds at standard workbench level 2 and costs 500 metal fragments, 20 refined metal,
and 200 wood. Its placement half extents are 0.89 × 0.939 × 0.463 metres, measured from the supplied
engineering bench mesh, with 500 health. These gameplay numbers are implementation tuning, not confirmed
design decisions.

Validation added: exact one-rock/one-torch starting and respawn inventories; reconnects do not duplicate
the kit; separate learning, station, and speed requirements; physical blueprint learning; client/server
confirmation and rollback of an unlock sent to the wrong bench. Existing ship assembly coverage now
includes the knowledge gate. Validation results are recorded below after the integration build.

## Validation log — 15 October 2026

- Installed tools: Godot 4.7.2 .NET, .NET SDK 10.0.302. Standard engine build.
- Solution Debug build and dedicated server Release build: zero warnings and zero errors.
  Eight inherited C# warnings were repaired without changing gameplay rules.
- Focused integration/regression run: 17/17 passed, including exact client/server ship prediction,
  Earth-scale movement and ascent/return, legacy scenario compatibility, and content validation.
- Full engine-free suite: **298/298 passed in 358.0 seconds**, including simulation, persistence,
  content, network authority, client prediction and new design-specific checks.
- Godot asset import succeeds. Real rendered captures at 1600 × 900 / High verify the survival menu,
  hosted Earth, finite galaxy map and Earth's system-map details.
- `Play.ps1` was run end to end: both builds, asset import and main-menu startup succeeded.
- Hosted server identifies Earth as body `8000000000000000/10300000`, radius 1,200 m, gravity 1 g.
  Restarting and rejoining the review save wakes the same character with its saved inventory.
- The first gameplay capture exposed an inherited muzzle-smoke warmup leak. Preview effects now have
  their own temporary owner; the fresh gameplay capture confirms the stray puff is gone.
- Known cleanup issue: hosted-game shutdown reports seven Texture RIDs and two ObjectDB instances
  still alive. Gameplay capture exits successfully without runtime exceptions; this is not a claim
  that all renderer resource cleanup is complete.

Review captures are under `docs/images/abtest-b/`; detailed transient build, import, server and test
logs are in ignored `.artifacts/`. These checks establish this development wave, not completion of
the full design or a production load/performance certification.

## Quality repair campaign — 15 October 2026

The owner's 17 current reports and 30 inherited reports are retained in `docs/reports/`.
Implementation and validation are tracked in [REPAIR_STATUS.md](REPAIR_STATUS.md),
[REPAIR_SHIPS.md](REPAIR_SHIPS.md), [REPAIR_CLIENT.md](REPAIR_CLIENT.md),
[REPAIR_SURVIVAL.md](REPAIR_SURVIVAL.md), [REPAIR_ENVIRONMENT.md](REPAIR_ENVIRONMENT.md)
and [REPAIR_ASSETS.md](REPAIR_ASSETS.md). Three specialists work alongside the
integration owner with separate file ownership and coordinated builds/imports.
This is active repair work, not a claim that every listed issue is solved.

Protocol version 28 rejects older clients: ship cruise/engine state, breathing and
admin flight-speed inputs changed, and shared cliff/monument geometry is now part
of authoritative collision. Player save version 6 preserves breath; ship save
version 4 preserves unattended cruise. Backwards decoding is covered by tests.

Current clean integration build: `.artifacts/repair-build5.log`, zero warnings and
errors. Focused runs have passed 20/20 gameplay checks, 26/26 broader regressions,
and 9/9 environment/audio/catalogue checks. The previous 298/298 full-suite result
belongs to the pre-repair baseline; a new full run is required before handoff.
The 32/32 actual Godot inventory input checks passed. Rendered captures found
additional bow/hand and submerged-grass problems; they remain under visual review
until their corrected captures are inspected.

## Salvaged Axe — 15 October 2026

The owner requested `woodsman_collection_heavy_duty_hatchet_axe.glb` from the local
asset library as **Salvaged Axe**, an upgrade above the existing metal hatchet.
The item uses key `salvaged-axe` and new stable ID 221; existing IDs are unchanged.
Its model is extracted from the supplied display scene, excluding the stump and
ground, and normalized for the shared right-hand grip and axe animation family.
The model catalogue, editable FPS/TPS profile, inventory icons, melee sounds and
animal harvesting table use the existing shared systems. The audio source table
also retains the new mapping when sounds are regenerated.

Initial balance is implementation tuning, not a separately confirmed design:
900 condition versus the metal hatchet's 600; tree harvesting power 65 versus 45;
25 melee damage versus 20; the same 1.7 m reach and 0.8-second swing. It retains
the metal hatchet's ore performance. Carcass harvesting power is 28, below the
specialist bone knife's 30.

Following DESIGN.md's advanced-variant rule, it starts unknown and has no standard
tech-tree node. Existing tier-2 loot-blueprint pools can supply its blueprint;
standard and military crates can rarely supply the tool itself (relative weights
0.08 and 0.12). After learning it, crafting takes 45 seconds at a level-2 standard
workbench and consumes 100 wood, 150 metal fragments, two metal blades and one
metal pipe. Existing shared research also permits researching a found axe for
125 salvage; rare-variant research eligibility remains provisional in the design.
Crafting uses raw materials: the inherited craft-cancel path refunds durable
ingredients at full condition, so consuming a worn metal hatchet would introduce
a free-repair exploit. No crafting-system change is included here.

Source: Gerardo Justel, [Woodsman Collection — Heavy Duty Hatchet / Axe](https://www.fab.com/listings/24947c95-1253-44a9-9b1f-0d057ed1ff72),
supplied in `D:/GameDev/Assets`. The asset-library licence authorization is recorded
in `handoff-ground.md` (15 October); the asset is not labelled CC0. Reproduction
script: `art/import/salvaged_axe.py`; source/output hashes and transformation details
are stored beside the installed material under `game/assets/items/salvaged-axe/`.
The optional `game-dev` CLI is unavailable, so this uses the project's local
Blender/Godot integration with a project receipt, not a canonical CLI admission.

Installed geometry is 0.62 m tall with 20,352 / 10,176 / 3,052 triangles in three
explicit LODs and a separate 114-triangle convex hull. Original 4K albedo, ORM and
normal maps are retained; the material preserves the source's double-sided flag.
The wider handle uses a 24 mm measured grip radius and 14 mm palm seat in its own
editable profile. The library source remains unchanged.

Validation: solution Debug and dedicated-server Release builds passed with zero
warnings/errors. Eight focused gameplay, model/icon, held-presentation and audio
checks passed. The gathering comparison took nine normal strikes with the metal
axe versus six with Salvaged Axe (five following the moving X), yielding the same
210 wood. Loot rolled both the tool and its blueprint; learning, tier-1 refusal,
tier-2 crafting and full output condition were verified through the shared rules.

Godot 4.7.2 imported the assets without errors and rendered both inventory icon
sizes. Actual Forward+ captures were inspected at 1600 × 900:
[model](images/abtest-b/salvaged-axe/model.png),
[first person](images/abtest-b/salvaged-axe/first-person.png),
[third person](images/abtest-b/salvaged-axe/third-person.png), and
[swing](images/abtest-b/salvaged-axe/swing.png). Character captures retain the existing
one-ObjectDB-instance shutdown warning. This does not certify the separate,
concurrently edited held-item animation system as complete.

The first build encountered in-progress shared-editor compilation errors. Small
namespace and delegate corrections in `HeldPresentations.cs` and `HeldItemEditor.cs`
unblocked compilation; other ongoing repair changes remain outside this addition.
Logs are `.artifacts/salvaged-axe-*.log`. Restart the client and hosted server to load
the new content; admins can use F1 `give salvaged-axe 1` to inspect it immediately,
or `give salvaged-axe.blueprint 1` to try the learning/crafting route.

Full engine-free suite: **330/334 passed in 483.3 seconds**, including both new axe
tests. Four failures remain in the shared checkout, so this is not a clean full-suite
claim: `ArchitectureTests.TheServerIsAHost` (1100-line host exceeds its strict
limit after the concurrent action-replication additions),
`BatteryTests.TheAimIsHeldInTheWorldWhileTheShipYaws` (new ship yaw acceleration
outpaces the unchanged mount), `ContentTests.BrokenDataIsReported` (the fixture
does not remove the newly added high-grade ore node), and
`NetTests.SnapshotsStayWithinBudget` (the expanded character payload fits 247/300
entities within the old 19-snapshot coverage bound). Source review traced these
to the concurrent repair/animation changes rather than axe content. Results were
reported to the user-authorized integration coordinator; no broader gameplay
repairs or commits are included in this asset handoff.

### Legacy L27 fuel tank — focused checks passed, visual validation pending

Ships now expose one fuel-only inventory slot through cap Open/general-ship Open fuel tank, backed by shared transfer rules and replicated slot constraints. Tank items and a separate partial-unit engine reserve conserve fuel through withdrawal, burn, saves and v1–4 migration into ShipCodec5. The final fuel fraction no longer grants a full powered frame. Six focused ShipFuelTests were added; existing ship inventory/persistence fixtures were updated. Root's coordinated fuel/battery/building/network run passed 68/68 (.artifacts/repair-focused5.log). Full-suite and visible fuel-panel capture validation remain coordinated by the root. See REPAIR_SHIPS.md for limits and accounting details.

### Legacy L15 corpse lifecycle — focused checks passed, visual validation pending

Death now leaves a supported, harvestable body even with an empty inventory. Shared melee/harvest rules and server time convert it into the same loot container rendered as a large backpack; inventory, partial harvest, appearance and support persist through WorldCodec7 and late joins. Client corpse rendering follows the server's supported pose and phase rather than a fresh local timer or unsupported ragdoll. The coordinated build9 passed with zero warnings/errors. All nine new rule/integration tests plus the updated connected-looter test passed; the broader corpse/animal/ship-support/HUD run passed 32/33, with a separate missing-barrel HUD fixture reported to its owner. Logs: .artifacts/repair-build9.log and .artifacts/corpse-focused.log. Corpse/lootbag captures remain pending. See REPAIR_CORPSES.md for provisional tuning and the prone-pose limitation.

## Public website and development archive — 15 October 2026

Updated and deployed https://biveno.com as Hollow Universe using the existing website
source at `D:/GameDev/Projects/RustMVP/website`. The original Hollow Universe checkout
was not edited. Current game copy follows DESIGN.md: Earth with one rock and one torch,
a maximum of 1,000 stars, compact planets and direct-flight ambitions. It distinguishes
local ascent/return from unfinished travel between worlds and preserves the ground UI direction.

Fifteen documented retrospective devblogs cover October 2–15, bringing the public archive
to 42 posts across 14 documented days. Older prototype records remain historical;
work dates and later publication dates are distinguished. The 298/298 compact-Earth
baseline and later 330/334 repair checkpoint are kept separate. Three existing reviewed
captures show Earth, the finite galaxy and the Salvaged Axe first-person viewer.

Website validation: build and checker pass for 66 HTML pages and 2,599 internal references;
35 local waitlist API checks pass; live read-only verification passes 22 routes including
all new articles, RSS, sitemap, canonical URLs, headers, www, old pagination links and 404.
Desktop and phone layouts and journal filters were inspected. No new game build or test
run was performed for this website-only change. Cloudflare deployed version
`f64b7e69-05be-455d-abf1-bf9e55e507cc`; existing waitlist storage and its three signups
were preserved. Publication receipts and screenshots are in the website's deployment/
validation docs and ignored `qa/hollow-*` evidence. No emails were sent.


## Website media follow-up — 15 October 2026

Updated the separate biveno.com website in RustMVP/website at the owner’s request: every one of the 42 devblogs now has media, 18 major entries have two or three images, and the rejected axe viewer capture was removed from the site and public asset output. Seven unmodified, visually reviewed development captures were copied from docs/images; concept art and historical captures have distinct captions. Website build/check passed (66 pages, 2,916 local references), desktop/mobile review passed, and live checks covered all 42 articles plus 18 images. The removed axe URL returns 404. Published Cloudflare version dd5ebb19-0bbe-441c-985d-c56e94d775ca; live waitlist count remained 3. No game implementation changed and no game tests were run for this website-only update.

## Throttle at first boarding — 15 October 2026

Investigated the owner's report that W/S could not adjust throttle before using boost. The old control loop
snapped every value within ±0.02 back to zero each rendered frame; its default 1.25/second lever rate could
never leave neutral above 62.5 FPS. Boost requests speed independently, explaining why it could still move
the craft. The existing working-tree repair already replaced that dead band with crossing-only neutral
snapping in `PilotSteering.AdjustThrottle`, called by the shared `ShipControls.Read`. Boarding initializes
the lever once from retained cruise and immediately accepts W/S; no boost prerequisite was found.

This follow-up preserves that production fix and adds four dedicated `ShipThrottleTests`: forward/reverse
from the first frame at 30, 60, 120, 144 and 240 FPS; crossing neutral and leaving it again; retained position
and travel limits; and sub-quantum frame changes accumulating into transmitted throttle without boost at
1000 FPS. No gameplay bindings, UI, simulation tuning or generated data changed in this follow-up.

Validation: solution build passed with zero warnings/errors (`.artifacts/ship-throttle-build.log`).
`dotnet run --no-build --project tests/Hollow.Tests -- Ship Architecture` passed **46/46 in 51.6 seconds**,
including all four new throttle cases (`.artifacts/ship-throttle-tests.log`). This was a focused run, not
a rerun of the full engine-free suite.
This follow-up has not performed a live cockpit keyboard retest or new visual capture; these checks exercise
the engine-free lever and wire conversion, alongside the existing ship suites.


## Website evidence and held-item workshop devblog — 15 October 2026

Published a dedicated held-item workshop devblog using the owner’s supplied bow-motion screenshot plus a fresh third-person capture from the current binary. Captured current ground gameplay with an isolated temporary database and port 27177; the client disconnected and server stopped. Both captures were visually reviewed. Capture exits logged ObjectDB/Texture cleanup warnings; this was not a new full build/test or animation-acceptance run. No gameplay source or profiles were edited. Website now has 43 posts; the 27 original articles and hero design retain their artwork per the owner’s clarification. Newer entries use matching captures/renders or labeled graphics of recorded test output; provenance is recorded in the separate website repo. Build/check and live 16-article/22-asset verification passed. Cloudflare version 1ba166e2-f421-4b96-8530-00bf22ccbc22.

## Functional attachment presentation integration — 15 October 2026

L17 optics/light now have bounded source integration: ADS selects the attached native eye before the base gun;
held magnification scales mouse sensitivity; optional model catalogue capabilities supply real lens/reticle and
beam presentation. The existing authoritative HeldMods and Light-while-held rule provide both local and remote
state, so no protocol or independent toggle was added. Two engine-free regression tests cover the GLB aperture
and two-client attach/detach/holster/death lifecycle. Only the two agreed held-view hooks were changed.

Corrected original holo/scope geometry is activated; native-eye ray tests pass every LOD, including removal of the
scope caps and a 3 mm lowering of the obstructing holo emitter. Coordinated build 13 passed with zero warnings/errors
and both new tests passed (2/2). Held reticles track exposure per instance; immutable lens materials are shared to
bound the existing first-person material cache. Build 14 adds the actual-input ADS/night-light capture scenario,
including final camera-ray checks through the real optic geometry. Rendered acceptance remains pending. Details and limits:
[REPAIR_ATTACHMENTS.md](REPAIR_ATTACHMENTS.md). No new build or GPU run was performed by this agent.

Build15 actual gates subsequently verified the complete corpse flow and the shared harvest-health bar; six accepted
death/body/loot/backpack captures are retained under `docs/images/abtest-b/repairs/corpses`. Holo ADS passed actual
native-eye, material and camera-ray checks. Scope ADS failed at 64.6 mm eye error because the arm reach solver
moved the aligned gun into the camera near plane. Build16 adds a fitted-optic-only, bounded shoulder accommodation
and actual 25 mm hand-contact guards; iron sights and reloads retain their prior path. Scope/night-light acceptance
and base gun regressions are pending that coordinated build/run.

## Repair wave consolidation — 15 October 2026, build 16

This entry records the current coherent repair wave and preserves earlier dated checkpoints above. Build 16
compiled with zero warnings/errors (`.artifacts/repair-build16.log`). The last completed shared full run remains
376/376 in 418.2 seconds (`.artifacts/repair-full2.log`), followed by two focused optics/light checks. A new final
full run is currently writing `.artifacts/repair-full3.log`, with 384 checks expected; its outcome is pending and
must not be inferred from compilation or the earlier run.

Actual UI input now passes 47/47 (`.artifacts/repair-ui-check3.log`). The two added checks exercise the real client
attachment action provider: draggable socket UI omits redundant REMOVE actions while preserving unload and other
actions. Inventory rotation, H quick-loot across panels, Unicode bed names and the prior map/UI gestures remain
covered. All three native window modes were previously verified. Build15's actual corpse sequence also passed
death, respawn, visible body, preserved inventory, real melee harvesting, shared damaged-target health bar and
conversion of the same loot entity into a backpack (`.artifacts/repair-gates15.log`). Six accepted frames, including
`repair-corpse-damaged.jpg`, are retained in `docs/images/abtest-b/repairs/corpses`.

The controlled build14 environment comparison supersedes the earlier rejected/uncontrolled grass/soil attempts.
Matching fresh-save captures held resolution, camera, player position and runtime inputs fixed, and root accepted
the bounded reduction in dry-ground sheen, restrained green coloring and retained vegetation coverage. The
continuous underwater underside repair is also accepted. Preserved `environment-stream-covered.png`,
`environment-soil-before.png` and `environment-soil-after.png` plus provenance are documented in
[REPAIR_ENVIRONMENT.md](REPAIR_ENVIRONMENT.md). Large flat legacy grass blades, coarse trees/rocks and the excessive
night star field remain visible limitations; this is not final environment realism or an FPS result.

Profiling exposed and fixed a separate surface-streaming address collision: the old full-scatter flag overwrote
a cube-face bit in the packed tile ID. Requests now use the complete tile plus a separate mode. Both
`SurfaceStreamingTests` pass, and the previously stuck teleport settles with zero missing tiles in actual
`profile-idle-3` and `profile-chop-3` runs. Build16 then limits natural rendering invalidation to visible presence
or model-stage changes. Four `NaturalRenderTests` plus those two request tests pass (6/6), covering ordinary hits,
streamed overrides, model stages, depletion/respawn, berry regrowth and normal/early-respawn tree-fall completion.

`.artifacts/profile-chop-4.log` confirms the bounded result in the engine: seven ordinary hits retain effects and
weak-spot cues with zero plant uploads or outcrop rebuilds; the eighth hit removes the standing tree, uploads the
739 remaining instances once, plays the fall and preserves the same 81 wood yield. A competing external import
overlapped the earlier chop baseline, and an external `GeologyReview2` render overlapped the candidate. Overall
timings therefore cannot support an FPS improvement claim. First-hit cue/marker setup and wider intermittent
stalls remain unresolved; full measurements and limits are in [REPAIR_PERFORMANCE.md](REPAIR_PERFORMANCE.md).

Final scope/night-light optics acceptance, repaired building interiors/joints and placement captures, animal
orientation/attack presentation and the complete nine supplied salvage/container visual batch remain pending.
Holo ADS has actual acceptance; compilation and engine-free attachment checks do not close the remaining optics
gates. No new source, asset, build or GPU work was performed for this documentation consolidation. Current package
ownership, evidence and remaining work are collected in [REPAIR_STATUS.md](REPAIR_STATUS.md).

## Optical image and honest night capture — source checkpoint for build 17

Build16 passed the actual scope native-eye, axis and 25 mm hand-contact checks, but its image was rejected: the
hollow physical tube still produced a tiny aperture with the front sight intruding. A magnified-optic presentation
capability now renders the shared world into the authored rear lens during local ADS, using one bounded 512² HDR
viewport with the first-person layer excluded. Its field of view matches the existing magnification and aperture
size; weapon geometry, grip poses and the accepted shoulder accommodation stay unchanged. The viewport stops
outside ADS and is owned by the held attachment. Its unique texture material is created after cached conversion.

The earlier light on/off captures did show the beam, but did not prove night: their clock calculation used the
landing-frame origin instead of the actual player's radial position. The hosted attachment scenario now chooses
midnight at that real position and requires the rendered sun below −6°. It also reads back the real scope image,
checks the shared world/cull mask/field of view and confirms rendering stops after ADS. Source and documentation
are coherent for root's next build; no compilation, GPU run or visual acceptance is claimed for this new renderer.
Read-only review identified two lifecycle gaps before that build; both are closed in the same source checkpoint:
the unique lens material follows live viewmodel FOV even outside ADS, and the optical environment mirrors dynamic
immersion fog from the main camera while rendering. No shared cached material or source environment is mutated.

## Optics and night-light acceptance — build 17

Root's actual hosted `repair-attachments` run passed every check and all six resulting frames were visually
accepted (`.artifacts/repair-optics17.log`). The scope now fills its rear aperture with a clear world view and
crosshair, without the gun front sight blocking the image. Holo, scope and night scope measured zero native-eye
and hand error at the printed precision, with axis alignment 1.000000. The actual 512² scope image used 7.36° FOV;
its extra render disabled when ADS ended. Verified local solar hour 0.00 and sun elevation −63.98° make the
light-off/on/detached comparison genuine night evidence: the beam visibly changes the same hut wall and appears
through the scope, then disappears after detachment. The six unchanged captures and SHA-256 provenance are in
`docs/images/abtest-b/repairs/attachments`; [REPAIR_ATTACHMENTS.md](REPAIR_ATTACHMENTS.md) links each frame.

Build17 compiled with zero warnings/errors and the final focused run passed 8/8, including both attachment tests.
The completed build16 full suite passed 384/384 in 429.0 s. Actual base pistol/rifle held-editor regressions after
the shoulder change also passed on both male and female bodies: 12 actions and 200 FPS/TPS samples each, with no
connected hand error above 25 mm. Their existing six Jolt-shape/one ObjectDB exit-leak diagnostics remain recorded.
This documentation/evidence checkpoint changes no runtime source and runs no additional build or GPU process.

## Integrated repair checkpoint — 16 October 2026

The coordinated repair wave is installed in AB Test B, with current status and remaining work in
[REPAIR_STATUS.md](REPAIR_STATUS.md). The original Hollow Universe checkout was not edited.
The final solution build18 and Release server build passed with zero warnings/errors. The engine-free full suite
passed 384/384 in 429.0 seconds on build16; subsequent changes are renderer/capture fixes and optical metadata,
with the final focused attachment/render-state/streaming run passing 8/8. Actual UI input passed 47/47; the engine
sound audit loaded 235 events and 783 files without a validation problem. Listening acceptance is separate.

The final build18 ship tour again passed fuel inventory, ordinary boarding, cockpit-to-orbit V, independent LMB
orbit drag, wheel zoom (6.00 to 7.06 m), W throttle, airborne Space exit and unattended powered flight. Current
cockpit/orbit hints were inspected and four unaltered frames retained under docs/images/abtest-b/repairs/ships.
The complete corpse/health-bar/backpack flow, actual holo/scope/night-light flow, nine supplied item/container
models, controlled soil/grass comparison, underwater, monument/cliff frames and both animal scenes have their
accepted evidence under the same repairs directory. The joint tour completed all 45 LOD0 views and retained
73 diagnostic images. Twig edge shimmer and untested building views/LODs remain explicitly open.

The broad environment art target is unfinished: coverage was preserved while adding variety and reducing sheen,
but legacy trees, rocks, large blade shapes, animal art and night stars still need work. Recorded animal sounds,
a deliberate weapon/audio feel pass, some smaller legacy interactions and clean competing-workload-free frame-time
measurements remain. The original reported native crash cannot be attributed without its stack trace; observed
exit resource warnings are recorded rather than suppressed. This checkpoint is not closure of all 47 reports.

## Audio-first continuation handoff — 16 October 2026

The owner requested a complete contextual handoff and made audio the first priority. The standalone prompt
`docs/HANDOFF_AUDIO_FIRST_2026-10-16.txt` enumerates all 17 current and 30 legacy reports, distinguishes prior
implementation/evidence from remaining quality and runtime checks, and preserves the larger design scope.
`REPAIR_STATUS.md` now reflects that ordering. Audio source verification, corrected routing and 22 new variants
did not complete the listening/mixing pass; the 235-event/783-file engine audit remains technical evidence only.

The preceding read-only runtime audit verified current AB Test B binaries and Maximum settings. The owner
confirmed grass is present after a live night screenshot showed dense dark cover. A separate moonlight cloud-ray
bug has a candidate patch and unrun tests under `.artifacts/cloud-shadow-stage`; no runtime files were changed.
The attempted copied-save night reproduction disconnected and was stopped, so it is not an accepted comparison.
The original user game/save were preserved. FPS remains available through the persistent F1 `fps` toggle; the
last inspected preference was false and was not changed by the audit.

This handoff is documentation only. Validation checks its complete R1–R17/L1–L30 coverage, referenced files and
diff formatting; no new build, test-suite, audio-listening or rendering acceptance is claimed.


## Scanned rocks and vertically filled cliffs — 16 October 2026

Integrated the isolated geology repair after checking each affected active file against its pre-edit hash. Nine authored rock variants (seven moss scans and two tundra boulders) replace the ordinary procedural boulders. Nordic cliff scans from the owner library now form overlapping contour columns and descending rows, following the owner's request to fill the full bare cliff face vertically. Shared server/prediction collision uses the same component transforms; terrain heights and natural-object IDs are unchanged. Source approach and two Nordic models were reused from RustMVP's local-scan replacement path; no Rust extraction or reference-map placement data was copied.

Verified in the isolated checkout: solution build with zero warnings/errors; GeologyPort, EarthCliffs and Architecture checks 8/8; 30 sampled formations, 402 components and 24 tiered faces. Actual Earth captures finished with streaming ready. A hosted game connected, drew the new geology, saved its capture and disconnected/stopped normally without loading timeout or renderer error. Its shutdown still reported 7 texture RIDs and 2 ObjectDB leaks; this is not a leak-free/full-suite/performance acceptance claim. Earlier failed captures exposed lossless texture imports and an export bug; final assets use shared external 4K maps with checked-in GPU-compressed/mipmapped imports and asynchronous cliff loading.

Evidence: `docs/images/repairs/geology/hosted-cliff.png`, `tiered-cliff.png`, `scanned-rock.png`; logs and exact integration hashes in `.artifacts/geology-port/`. Details/reproduction/limits: `docs/REPAIR_GEOLOGY.md`. Ordinary rock colliders remain the existing approximate spheres. Talus, vegetation pockets, broader terrain/grass/tree quality and global composition remain unfinished. The active running game was not stopped or rebuilt; restart through `play-ground.cmd` to build and import this source/assets update. No unrelated files or original Hollow Universe/RustMVP files were edited.

## Seated orbit mouse correction — 16 October 2026

At the owner's correction, all seated third-person orbit cameras now use held right mouse to change angle.
Left mouse retains the seat's normal primary action, including turret fire while orbiting and across V camera
changes. Right orbit drag does not alter steering or turret aim, and a held right button cannot become station
zoom on a camera switch until released and pressed again. Pilot/passenger/station hints now name RMB; the station
hint still shows LMB FIRE. The first-person ground interface and existing on-foot actions are unchanged.

Validation: isolated-output solution build and final client build passed with zero warnings/errors; focused
ShipRepair/Station/Architecture selection passed 14/14. The actual hosted repair-ships tour passed all 36 checks,
including pilot button independence/steering/zoom/exit and ordinary Iron Wake station boarding with outgoing
fire/zoom input checks. No ammunition was loaded, so this is input-routing verification, not a projectile test.
Screenshots were inspected and retained in docs/images/abtest-b/seated-camera; details are in REPAIR_SHIPS.md.
Logs/builds are under .artifacts/seat-camera. The first tour exposed a test timing error (the steering sample
preceded queued MMB delivery), which was corrected; two subsequent landing-site runs disconnected during loading.
The final run at 40.6263, 99.4565 completed normally. Existing SSS/7 texture RID/2 ObjectDB exit warnings remain.
No full-suite or performance acceptance is claimed. Validation used separate binaries and fresh saves; the
owner's running game was preserved. Restart through play-ground.cmd to build and use the corrected controls.

## Normal-ground audio and action feedback — 16 October 2026

The owner rejected the first thump-removal comparison: the boink remained throughout, especially walking/jumping.
After auditioning four dry and four organic contacts in a new six-second reel, they replied **"Neither set has it"**.
Those exact source selections now replace grass/dirt walk, run and crouch; the first three supply single-contact
landings. Generic soft-heavy impact layers are removed from footsteps. The new short contacts avoid lowered gait
pitch and stacked landings; pitch/jitter are constrained. Dry contacts received additional gentle compression after
the approved reel to satisfy the unchanged level bounds. Source-sample feedback is not final-file/gameplay approval.
See [REPAIR_AUDIO.md](REPAIR_AUDIO.md) for the filters, rejected attempt and acceptance boundary.

Three action defects also changed: one owner for bow-release playback, accepted-hit weapon identity for melee
contact sounds, and data-driven empty-trigger feedback once per press. Shared movement, authority, shuffle bags,
attenuation and voice budgets remain. There are 52 modified footstep files, eight obsolete files/imports removed,
and 96 unchanged footstep/swim files. The [whole-catalogue audit](reports/audio-action-checklist.md) enumerates all
235 events / 775 files and inspected action routes; exact recipe/source hashes cover the 148 footstep/swim outputs.
Unconsumed shell/repair actions, animal recordings and broader combat/material/mix listening remain open.

Technical verification: clean solution build and Release server (zero warnings/errors); 389/389 engine-free tests
in 442.1 s before the final asset-only revision; 10/10 audio/routing/catalogue tests after it. The final solution
rebuild includes recorder cleanup/duration guards. Godot's final sound audit loaded 235 events / 775 files with
zero problems, largest measurement difference 0.9 LU and highest true peak -1.14 dBTP. All 263 source downloads
matched their recorded hashes. No measurement limit was relaxed. The initial build failed on a concurrently edited
held-pose dependency and passed after that dependency arrived; the full recipe enumeration was stopped before
asset writes and replaced by the bounded shared-encoder command, not counted as a successful build.

Two isolated, fresh-save, ordinary hosted clients at 31.61184/132.47481 (High, 1280×720, hour10) completed the new
opt-in `audio-movement` tour without a loading timeout. Each walked 11.25 m, ran 21.81 m, crouched 5.20 m and produced
exactly one landing event for each of three jumps, with no idle steps. The first run's unedited
[Master mix](audio/repairs/2026-10-16/gameplay-grass-mix.wav) and
[Footsteps bus](audio/repairs/2026-10-16/gameplay-grass-feet.wav) were 26.848/26.933 s, nonzero and without clipped
PCM samples. The revised recorder stops both buses before saving; its repeat produced matching 26.848 s files
(26.858 s measured scenario), valid PCM activity and passing displacement/cadence checks. Start-request timestamps
are not sample-accurate audible onset. These runs cover grass and no remote listener; dirt and other material
gameplay auditions remain open.

After the initial audio embed failed to display, the recording was supplied through the native player and a direct
file link. The owner confirmed **"okay gameplay-grass-mix.wav doesnt have it"**: boink absence is accepted for that
actual grass gameplay recording. This does not approve the second verification recording or the wider audio mix.
The [approved source reel](audio/repairs/2026-10-16/approved-source-contacts.wav), raw game recordings, traces,
validation logs and hashes are retained under `docs/audio/repairs/2026-10-16`. Four unchanged start/end screenshots
were inspected under `docs/images/abtest-b/repairs/audio`: ordinary rendered ground, displacement and preserved HUD
are visible, but these are not environment quality acceptance. Both runs retain the existing seven Texture RID
and two ObjectDB exit warnings; no leak-free or broad performance claim is made.

Root serialized shared builds/imports/GPU runs and then released the slot to the held/environment work. Their
unrelated edits, geology integration and original-project files were preserved. The environment coordinator owns
reopening the user's normal session after the remaining shared runtime checks. This batch does not close the
complete audio programme, R9/L19/L21, the environment/held quality work or all 47 reported issues.

## Held-item workshop and gathering tools — 16 October 2026

The owner rejected the previous workshop result as nearly unchanged and selected rock/gathering-tool
carry, grip and swing. Reauthored six profiles: rock, stone/metal hatchets, salvaged axe and stone/metal
pickaxes. Carries now keep the holding hand visible, rock/axe/pick swings have separate paths, and
measured primary-hand curls replace the hanging thumb and overcurled finger templates. All other
profiles and all authored durations/markers are unchanged; no simulation reach, damage or cooldown changed.

The workshop now displays actual primary-palm gap, wrist bend and authored reach correction, with
an undoable finger-fit control and practical launch/edit/save instructions in HELD_ITEMS.md. Finger
fitting uses the selected body's measured cylindrical grip; support/open grips keep preset fallback
curls. Fallback posing stays allocation-free. Its UI regression seeds a different pose, presses the
real fit button and checks undo; another regression proves a 30 mm offset reports an actual palm gap
even when the old wrist-to-target metric passes. Optional limits also check projected grip framing.

Validation: solution build with zero warnings/errors (`.artifacts/held-tools-review/build-final.log`);
focused Held/Content/Architecture 27/27 in 21.6 s (`focused-tests.log`); final data recheck 3/3
(`final-data-tests.log`). The shared audio full run 389/389 in 442.1 s preceded later held diagnostic/test
edits and is an earlier checkpoint, not a final full-suite claim. Final male/female selected checks
each pass 24 actions / 404 FPS/TPS samples (`review-male.log`, `review-female.log`) under 8 cm reach
correction, 25 mm palm gap, 65 degree wrist diagnostic and 6% projected grip margin in idle/swing.
Max correction 0 cm / 7.11 cm; max wrist 61.6 / 64.6 degrees; palm gaps round to 0.0 mm.
These are sampled bounds, not clinical joint limits
or whole-mesh visibility tests. TPS transforms are finite and visually inspected, not contact-measured.

First renders were rejected for cropped hands. The final six FPS/TPS sheets, clothed female pick
sheet, workshop screenshot and three 1280x720 gameplay-timed swing sequences were inspected, including
independent finger/grip review. Retained in docs/images/held-items/2026-10-16; each MP4 repeats its
24 fps sampled cycle four times. The recordings use gameplay rigs in the workshop, not a hosted
combat test. The evidence manifest records source hashes and reproduction commands.

Owner visual acceptance remains pending. Salvaged-axe wind-up still crops its head and metal-axe
contact has a thin silhouette. One-handed free-left FPS motion remains unsupported. Existing
ObjectDB/Jolt exit warnings remain (one ObjectDB and one or two Jolt shapes in these runs).
This six-tool candidate does not close all 41 profiles, broader animation quality or other repair work.

## Held-workshop rejection and animation-source audit — 16 October 2026

The owner rejected the latest six-tool result and said the workshop is not usable to correct it,
including difficulty dragging through the UI. Earlier assistant visual judgments and numerical
attachment checks did not establish acceptable animation. The drag failure was not reproduced
with actual mouse input in this follow-up; source inspection confirms only an opt-in translation
handle, with no direct bone posing or rotation handles.

Read-only audit: the MakeHuman/MPFB character uses a 53-bone game_engine rig; 37 Quaternius UAL1/UAL2
clips plus six scripted clips already ship. The downloaded Standard packs include CC0 licenses,
and chop is already retargeted from TreeChopping_Loop. FPS resets its skeleton and solves arms to
JSON item transforms instead of playing these clips. Profile events also bypass normal TPS action
clips. CharacterAssets.LoadAnimations retains rotations and pelvis translation only, discarding
prop/camera translations needed by many first-person packs.

Recommended direction, not an implemented or approved design: select source motion before
integration; prove one axe sequence using authored skeletal playback, preserving arm motion and
using IK only for bounded grip corrections. Keep gameplay authority and timing unchanged. Existing
full-body chopping is a pipeline test candidate, not evidence of finished FPS quality. A dedicated
first-person tool pack or animator-authored arms/prop clips may still be required. The workshop
should support preview/clip assignment/offsets rather than require the owner to author animation.

Official source checks: Godot supports humanoid retargeting; Quaternius lists Godot exports and
CC0; Adobe lists Mixamo as free with royalty-free game use. The locally recorded Epic Animation
Starter Pack listing explicitly says UE-Only, so it is not a Godot source candidate. No assets were
purchased/downloaded/copied, no runtime profiles changed, and no build/GPU process was started.
This follow-up changes status documentation only; git diff --check is the relevant validation.

## 16 October 2026 — verified environment integration

Installed seven authored fir/birch/pine variants, preserved native material channels and shared
standing/falling trees, corrected pine crown simplification and taper after rejecting real renders,
and retained the validated chopping band. Rebalanced existing grass heights and native ground
scan colour without removing cells/instances; corrected periodic wave normals and per-light cloud
shadows; reduced the procedural night star field. Existing geology work is preserved.

Normal child-host Earth1200 route completed baseline/final High-day and Maximum-night at 1600x900.
All six checkpoints were ready and grounded; no route teleport/noclip. Original frames, raw
samples and measured regressions are retained under docs/images/repairs/environment-2026-10-16.
High woodland median 15.50→17.68 ms; Maximum/night woodland 28.04→36.20 ms, so dense-canopy cost is
explicitly unfinished. Broadleaf art, foam, night readability and exit leaks also remain.

The pine chop tour caught Windows child-pipe degree-symbol decoding and false-positive screenshots.
Explicit UTF-8 pipe/redirected-input encoding plus arrival/hit/depletion assertions fix the diagnostic
and actual hosted command path. Final tour accepted hits/X chain, depleted the tree, awarded 125 wood
on its last hit, and captured the new falling mesh. User saves were not used for diagnostics.

Build 0 warnings/errors; imports passed. Full tests 389/391 with both timing-sensitive failures passing
isolated 2/2; final Console/Gather/EnvironmentRoute 18/18. Independent final pine 128/128 rays and
source-card/UV/winding/hash checks passed. Later shared-checkout suites passed 392/392 and 394/394;
their logs preserve those later checkpoints. Forced near/medium/far tree captures loaded successfully,
but medium birch density and distant foliage brightness still need tuning. This is not finished
art quality. See docs/REPAIR_ENVIRONMENT_2026-10-16.md, REPAIR_TREES.md and ENVIRONMENT_ROUTE.md for scope,
rejected versions, reproduction and limitations. Audio was validated first in the coordinated
chat; held-item animation is separate ongoing owner-directed work.

## Quaternius metal-hatchet pilot and smaller FPS framing — 16 October 2026

The owner requested trying the installed Quaternius animations instead of replacing their motion
with held-item curves. They subsequently described the test as much better but the axe/hand as
too large. The new pilot enables only metal-hatchet. UAL2 TreeChopping_Loop (runtime chop) drives
actual rotations in both views; the tool follows the hand, with static holding-finger fitting.
FPS skips arm IK and the old item/shoulder/wrist curves; TPS samples upper-body rotations after
locomotion and preserves whole-body action priority. Missing sources report a fallback explicitly.

The wrapped source cycle begins and ends at its 0.43-second recovery pose, also used as frozen
carry. Geometric slowdown at source 0.166667 seconds supplies an inferred contact marker, mapped
to the unchanged gameplay strike tick. The FPS rig offset is (-0.04,-0.14,-0.25) metres with
25 degrees X rotation: farther away and lower on screen. Source arms include the upper-arm mesh;
initial captures exposed the old elbow cut, so those captures were rejected before final framing.
Workshop placement and translation handles now edit the complete source rig. Ignored motion-key,
wrist, shoulder and elbow fields are hidden; source identity, holding grip and fingers remain.

Verified installed inventory: Standard UAL1 has 46 clip entries and Standard UAL2 has 43, including
reference poses and variants. Runtime contains 37 Quaternius clips and 6 scripted clips, not the
full 120-animation edition advertised by the store. No new assets, downloads or purchases.

Validation: solution build: 0 warnings / 0 errors. Male editor checks passed 8 actions / 164 samples (source
hatchet plus legacy stone hatchet); female passed 4 source actions / 100 samples. Every sampled source
upper arm, forearm and wrist matches its imported local rotation within 0.1 degree, including
perturbed legacy curves and actual gameplay strike entry points. Numeric controls, UI-event gizmo
drag, undo/redo and temporary save/reload preserve source bindings/rig offsets. Every final video
frame and both final sheets also run source-fidelity assertions. Initial focused suite: 27/28 had
one observer reload timeout; its isolated rerun passed 1/1. The final complete suite passed 392/392 in 420.4 seconds.

Evidence: docs/images/held-items/quaternius-trial contains the smaller FPS and TPS recordings,
male/female sheets, workshop screenshot and hashed evidence.json. Movies repeat a 20-frame 24 fps
capture six times (0.8 seconds sampled, 0.833 seconds encoded per repeat). Scope limits remain:
full-body windup leaves FPS frame; carry/equip/sprint are held source poses, with no gameplay
sway/bob/equip/sprint overlay or spatial contact adaptation. Other held profiles remain unchanged.
The original physical mouse failure was not reproduced; injected UI events are not a claim of
manual drag verification. One ObjectDB shutdown warning remains. Wider animation quality and
workshop usability are still open; this records a bounded trial, not final owner acceptance.

## 16 October 2026 — 97% loading fix and shadow budgets

The final steady-frame loading stage could wait forever below 30 FPS after all assets were ready.
It now has a data-driven five-second deadline after dependencies complete; its normal smooth-frame
path remains. Gib warm-up now uses previews owned by the loading stage, fixing the reported null
scene-tree error from the deferred live gib singleton. Diagnostics also restore GUI/window state
when capture isolation exits and keep timing uncapped after temporary graphics previews.

Added the SHADOWS tab: independent sun cascades 1/2/4, shadow quality/distance, maximum local shadow
lights 0–8 and local influence distance. A shared camera/world-aware budget covers authored ship
lamps, weapon attachment beams and torch light, with held-light priority, stable distance selection
and automatic cleanup. Low/Medium/High now use 1/2/2 sun cascades rather than always 4. The moon keeps
one cascade and 300 m maximum reach. Existing saved settings remain intact until changed. Baked
shadowmask remains unimplemented because there is no valid static lighting bake for this world.

Isolated copies of the player's save at the same pose/time, 1600×900, RTX 2070 SUPER: final Low 185.0 FPS
(mean 5.41 ms, p95 6.31 ms, GPU 4.48 ms), Custom about 61 FPS. Low uses 75% render scale and reduced detail;
this is not a shadow-only optimization gain or proof against the unrecorded early-project baseline.
Custom two-cascade shadows reduced draw calls 1,561→1,161 and mean 16.40→15.73 ms; tighter distances
cut geometry further without a reliable additional GPU gain. Grass/SDFGI remain meaningful costs.
Rejected spatial tree batching because it increased calls without demonstrated benefit. Final
Low reached play in 19.91 seconds; ground runs still report pre-existing shutdown resource leaks.

Validation: solution build: 0 warnings/errors; focused loading/architecture/local-shadow 19/19;
Godot shadow/torch/capture-isolation check 59/59, with unchanged preference hashes. SHADOWS UI and
forest screenshots inspected. Final full suite: 397/398 in 418.3 seconds; the existing
`HeldActionTimingTests.MeleeHitMarkerCarriesTheAuthoritativeToolTickAndContactPoint` failed to start
its swing in the full run, then passed alone 1/1 in 4.8 seconds. This is not a clean full-suite pass;
the unrelated timing test was not changed by this repair. Earlier loading-only checkpoint passed
394/394. Both final logs are retained with the evidence. Evidence and
reproduction: [REPAIR_LOADING_SHADOWS.md](REPAIR_LOADING_SHADOWS.md) and
[raw samples/screenshots](images/repairs/performance-2026-10-16). This is a bounded loading/shadow
repair, not completion of dense-canopy performance work or a guarantee of 80–144 FPS in every scene.

## Combat synchronization investigation — 16 October 2026

Investigated the owner's melee/hit-feedback concern and three visual shots from the Iron Wake's
two-barrel cannon. No gameplay repair was made. Deterministic production-client probes reproduce
three firing events from two chambers, while the real authoritative station fires twice and
refuses the third press. Two causes are established: confirmation removes predicted chamber
reservations before matching state, and fresh siege stations predict the opposite first barrel.
The latter also reproduces with the actual first server shot and updated state delivered.

A real simulation/local-melee probe reproduced a one-tick start disagreement across two
damaging swings; the FPS contact guard rejects the mismatched action ticks. It also reproduced
ignored fast taps carrying only PrimaryPressed. A shoulder-launcher probe using real LocalGun
prediction and serialized message dispatch restored ammo on refusal but retained the visual
rocket. Lost launch, lost endpoint and endpoint-before-launch each leave the rocket without
terminal reconciliation; an ordered baseline binds one projectile correctly. These tests expose
presentation/prediction defects, not extra server-authoritative ammunition or damage.

Solution build: zero warnings/errors. Existing HeldActionTiming/Gun/Battery/Raid/BowPresentation
tests: 41/41 in 70.3 seconds, including the previously intermittent melee hit-marker test. Three
standalone diagnostic projects reproduce the reported gaps; their successful exits assert the
faulty current behavior, not a fix. No full-suite rerun or Godot capture was made, and the user's
running game, saves, original checkout and unrelated environment-document edit were preserved.
Local sources/logs are retained in .artifacts/combat-sync-audit. See
[COMBAT_SYNC_AUDIT.md](COMBAT_SYNC_AUDIT.md) for exact scope, evidence, remaining source findings,
and the proposed shared attack identity/lifecycle repair. Known-problem status remains open.

## Consolidated owner playtest and parallel repair assignments — 16 October 2026

Merged the original thirty and later seventeen reports with twelve new owner reports into
REPAIR_BACKLOG.md, preserving source IDs and distinguishing missing implementation, pending
gameplay verification and previously checked behavior. Retained eleven original screenshot
files with dimensions/SHA-256 manifest; images 6 and 7 are identical. New owner acceptance
of the observed axe motion is recorded; pickaxes, other tool framing and oversized holo remain
open. Cliff composition, terrain repetition and grass quality are explicitly rejected.

Read-only investigation found PSG_ResourceNodes_R01 and matched all twelve source blends and
twelve installed stage GLBs to the integration receipt. Decorative boulders have no yield;
screenshot 3 is not yet identified as a runtime entity. Existing geology assets/code/captures
survive despite unavailable source-chat history. Bullets already have speed/gravity/drag;
tracer visibility is a distinct task. Iron Wake's current auto-level explains the unwanted
return to level; the requested repair is chosen-angle hold with assist ON. Roof gables exist
in both mesh and collision. Gib limits are currently catalogue-only.

Prepared eight scoped package prompts with three-agent splits, acceptance criteria and shared
execution rules. After verifying the actual human coordination request in each worker chat,
assigned P1 cliffs to 01a1447a-bf01-7cb2-84b4-ff315f4a61bf, P2 environment to
01a1447b-26d5-7211-aeed-ec8f7e9c4dc8 and P4 first-person/optics to
01a1447b-3abc-7672-a5c7-571c01807f1d. Existing combat, Earth terrain, admin and disconnect
owners retain their active files. No gameplay source change, build, import or runtime launch
belongs to this documentation/dispatch batch. Verification is report-ID coverage, links,
image hashes and diff checks; no new performance or full-suite pass is claimed.

## Recovery coordination after computer restart — 16 October 2026

The owner reported a computer crash and asked all interrupted chats to continue. Windows
reported a 15:10 boot; the recovery inventory found roughly 16 GiB physical memory and an
already open Blender. The cause of the crash is not established, while prior validation logs
record memory failures and concurrent test/render runs. Some latest combat log tails contain
NUL bytes and interrupted suites have no final summary; these remain invalid completion evidence.

Most saved source, staging and report images survived. Before resuming workers, preserved binary
staged/unstaged patches and 65 new files (about 45 MB) in
`.artifacts/recovery/2026-10-16T121759Z`. Resumed six interrupted leads (combat, Earth,
admin-camera, P1, P2, P4) and assigned the previously free worker P8's resource/validation
inventory. The completed library audit and committed disconnect feature were not rerun.
RECOVERY.md temporarily requires lead-only work, one global heavy-job slot and exclusive Git
index access. Existing camera staging was preserved. The merged docs pass all 59 report-ID mappings, internal
links, eleven image hashes and diff checks; `.artifacts/recovery/docs-validation.json` retains
the result. Package quality and final integrated validation remain open.

Later byte inspection found a NUL-filled cliff renderer and three P2 scratch files; the owners
preserved corrupt originals and reconstructed them. A broad post-repair scan of 124 changed/new
text files found no remaining empty/NUL files. Commit 86d2d9f1 preserves the merged queue/prompts
and original screenshots; the existing 25-path staged camera binary patch was byte-identical
before and after that path-only commit.

The first coordinated recovery build froze 113 source/data inputs and used one MSBuild worker,
no build servers/shared compiler, a time bound and memory monitoring. It was stopped after
34.69 seconds at 1.566 GiB available RAM and 74% commit. DLL emission without a final solution
summary is not a build pass. No tests, game, GPU/import or load run followed. No owned build
processes remained; user applications stayed open. RECOVERY.md now requires 5.5 GiB available
plus an explicit single-job grant before retrying; light source work may continue. Logs and
receipts are linked from that note. This memory-pressure observation does not establish the
cause of the earlier Windows bugcheck.

At 17:12 the owner confirmed Blender export work complete and requested resuming the chats.
Available RAM was 9.1 GiB / 37% commit with no build/game/Blender process. Seven development
leads resumed with one-heavy-job scheduling. An initial compile found a missing Hollow.Client
namespace in the new clan tour; its owner repaired it. The shared incremental solution then
passed with zero warnings/errors, 12.87 seconds, minimum 5.983 GiB free / maximum 48% commit
(`.artifacts/recovery/integrated-checks/20261016-171805-build.*`). P5's focused run completed
64/65 and exposed reordered gun inputs producing an extra predicted shot. The correction and
three deterministic regressions are now written but unvalidated, invalidating that source
checkpoint. The next foundation run did not start because its 4.902 GiB preflight was below
the retained floor. No fresh full-suite or visual pass is claimed.

The new compound-kit/inventory review is recorded in repair-results/Asset-recovery-review.md.
All 228 compound delivery entries hash-check; twelve real assemblies exist. The wider transfer
contains mixed source formats, support files and explicitly excluded extracted Rust content.
Following the owner's LOD question, the integration guidance uses the existing Godot automatic
mesh LOD for new rigid props where suitable, with authored LODs as optional quality fallbacks;
no custom Nanite renderer exists in this project. Collision/sockets remain separate concerns.

## F1 admin BGrade and free construction — 16 October 2026

Added BUILDING controls under F1 → TOOLS. BGRADE selects OFF, Twig, Wood, Stone,
Metal or Armoured from the building catalogue. Structural pieces are placed at the
chosen tier with full health. FREE BUILD starts on for admin sessions and can be
disabled; it covers construction, upgrades, hammer repairs, keys and repeated
deployable placement. The required tool/item still selects the action. Explosive
charges remain consumable, and reach, collision, support, claim authorization,
ownership limits and repair cooldown remain enforced.

Free crafting exposes all recipes, bypasses ingredients, learned blueprints and
workbench requirements, and keeps ordinary crafting durations and queue limits.
Ships crafted freely without a launch bench become placeable inventory items.
F1 controls, crafting controls, placement ghosts, plan/hammer radials and predicted
inventory payment read the server-confirmed state. With FREE BUILD off, BGRADE
charges the chosen tier's materials. Admin revocation and disconnect clear the
session flags; reconnect starts free ON and grade OFF without writing cheats to saves.

Protocol 30 adds authoritative construction state and a free-payment marker per
craft job. Player save version 7 retains payment provenance and delivery station,
reading older jobs as paid. Cancellation/death never refunds unpaid inputs even
after disabling free crafting or reloading. No galaxy output/golden values changed.

Validation was isolated in a managed worktree because separate combat/menu work
was changing shared source files during this task. Only this feature's patch was
applied there. No original-project files or player saves were used. Screenshots
under docs/images/admin-building are Godot UI-viewer captures (1600×900), visually
inspected; they are not a claim of manual gameplay testing. Engine-free loopback
tests exercise the actual server/client commands, prediction and placement.

Validation: the isolated solution build passed with 0 warnings/errors; the complete
engine-free suite passed 406/406 in 461.4 seconds. Review then corrected disconnect
cleanup to use the persistent player ID after the host detached the entity; the
final rebuild passed with 0 warnings/errors, and all nine admin regressions passed
in 8.5 seconds, including reconnect defaults. Godot `--view ui --ui-check` passed
51/51, including free crafting/MAX/cancel/off interaction checks. The visual changes
were unchanged by that final lifecycle fix. Logs are in .artifacts/admin-building.
Reproduce the focused checks with `dotnet run --project tests/Hollow.Tests -- AdminBuild`;
viewer captures use `--screen console-tools-stone` and `--screen crafting-free`.

## Disconnect from the ground menu — 16 October 2026

Added DISCONNECT to the connected Esc menu, in the existing CONNECT position. It returns to
PLAY / CONNECT with the address form open, without exiting the application. QUIT remains
available. Disconnect uses the existing client/server shutdown: a locally hosted server saves
and stops, while a remote server remains running. Unapplied settings previews are closed first.
The old menu/view now leaves the scene tree synchronously before a replacement is created;
this prevents old exit hooks from clearing the new session's inventory preview, audio and
planet render globals. Connection preferences and the existing ground UI layout are retained.

Validation: final solution build passed with 0 warnings/errors; Architecture, Loading and
Multiplayer tests passed 20/20 in 27.9 seconds. A temporary native Godot runner drove real Esc
and mouse events through HOST SOLO, DISCONNECT, CONNECT to a second independent server,
DISCONNECT and HOST SOLO again on the original port. It verified client/view teardown, visible
cursor/address form, hosted process exit, independent server survival, restored inventory-preview
registration and a saved player waking on rehost. This check passed in 32.2 seconds with 3D
presentation updates suppressed and the real client network pumped by the runner; it does not
validate fully rendered world transitions. The normal renderer captured the actual GroundUi on
the UI viewer's sample backdrop: [connected menu](images/disconnect/connected-menu.png), inspected
at 1600 x 900. Temporary diagnostic hooks were removed, and the final production build passed.
The saved ground-ui.cfg SHA-256 matches its pre-check backup; all servers used temporary databases.

The attempted full suite ended out of memory after 381 successful tests, with no final suite
result. Full-world attempts also encountered startup transport timeouts and a rendering allocation
failure during concurrent work; see KNOWN_PROBLEMS.md. A headless rendering attempt produced RID
errors, so the passing lifecycle check used the native renderer with world updates suppressed.
One ObjectDB shutdown warning remains. None of these runs establishes a clean full-world or full-suite
pass. Local runner, build/test logs and temporary saves are in .artifacts/disconnect-check.

## 16 October 2026 — Asset library and gameplay availability audit

Audited the local asset library against AB Test B's importers, model/data catalogues and runtime
consumers, with read-only comparison to the original project. The five remembered window-cover
assemblies are already in window_inserts.glb but remain art-only; wooden and armoured double doors
also lack player item/recipe/placement entries. Other reusable candidates include the starter solar
panel, four miner deposits, unused scenery/variants, 52 monument archive variants outside live
selection, vegetation and Quaternius clips. Seven downloaded animal packs lack located provenance.
The existing building kit, ships, resource-node stages and recent salvage/environment integrations
were distinguished from unused assets; old production notes were not assumed current.

Evidence: [ASSET_LIBRARY_AUDIT.md](ASSET_LIBRARY_AUDIT.md) and
[structured inventory/hash checks](reports/asset-library-audit-2026-10-16.json). All 142 base model
file references resolve; all 121 monument GLBs match the original. All 118 installed/source receipt
pairs match. Fresh checks of 1,077 source-manifest entries found four WreckModules discrepancies:
project.godot and three texture import sidecars; the other five Hollow packages match fully.
Wreck revision 2 addresses the earlier review requests but still needs current review/integrity
reconciliation. No assets were installed, runtime code changed, processes started, or saves touched.
Validation was file/data/GLB/source inspection and SHA-256 checking; no build/test suite or Godot
playtest was run for this documentation-only audit. Existing unrelated work was preserved.

## Earth land proportion and connected continent — 16 October 2026

The owner approved more Earth land and larger connected landmasses within the 23.04 km² total
surface cap. The shared Earth-only walking profile now targets 35% ocean, a 3,200 m continent
wavelength, 0.12 island share and 0.02 coastal detail. Radius remains 1,200 m: 18.0956 km² total.
Physical planet metadata and every other world's heightfield remain unchanged. At 160,000
equal-area directions, dry ground increases from 5.12297 to 10.64472 km² (28.31% to 58.825%).
An independent cube-sphere sample at about 15 m finds 99.9% of dry ground in one 10.64 km² component;
this does not promise that every point is walkable or buildable. Earth golden is 3D5174066B119948.

The land change initially used earth-v2 default saves. P1's subsequent cliff-collision integration
advances Earth to save revision 3 and every other walking body to revision 1, with earth-v3 default
filenames; existing files are preserved. Schema 9 stamps terrain identity by body and rejects
incompatible saves read-only before migration or restore. Current walking bodies no longer adopt
revision-zero spatial state; stand-in ground keeps revision zero. The refusal explains that solid
cliff placement can change while heights remain fixed. P5 owns protocol 33 for matching collision.
Three additional compatibility regressions are written and queued for the integrated build/test
slot; no post-restart runtime result is claimed here. The map cache already fingerprints sampled terrain.
System-map ocean presentation uses the playable target. The new `GeneratorReport earth` path
measures/maps actual Earth; `walking` remains the historical first-world report.

Solution Debug and server Release builds: zero warnings/errors. Initial focused terrain/environment
suite: 20/20; final save/architecture checks: 10/10. The first shared-output full run was rejected
after concurrent source/build changes and a native process failure. A fresh isolated binary copy
passed all six previously failed/interrupted checks (6/6). Its full run was interrupted by the
computer restart after 253 passes, one GunNetTests.GunfightOverTheNetwork failure ("reloading at
once") and one unfinished IronWakeNet test. No full-suite pass is claimed. Logs are preserved;
post-restart integrated validation requires the recovery coordinator's exclusive runtime slot.
Actual 1600×900 Low-quality child-host beach and orbital captures reached Playing/ready with loading
complete; system-map and before/after biome maps were inspected. The usual seven Texture RID and
two ObjectDB shutdown warnings remain. Two High-quality inland-start captures timed out during
asset loading and are rejected; this reproduces the separately open startup-liveness problem.
The new 786.5 m environment route passes heightfield checks but has not been driven by the hosted
route harness. No user save was used for validation. Details/reproduction: [EARTH_TERRAIN.md](EARTH_TERRAIN.md).
The owner's follow-up about generating candidates is feasible: seeds/profiles can be compared and
then fixed for the chosen world. A reroll UI was not requested or implemented. Static recovery and
the bounded screenshot-5 diagnosis are in [Earth-terrain-recovery.md](repair-results/Earth-terrain-recovery.md).

## F1 admin self camera — 16 October 2026

Added F1 → TOOLS → CAMERA → THIRD PERSON and local `thirdperson [on|off]`, with
`tps`/`selfcamera` aliases. It shows the full local character with clothing, held
items and stance/movement using the same renderer as other players and seat cameras.
Hold RMB to orbit independently of the character's aim; wheel zooms 1.5–20 metres.
Movement remains available, while held actions and interaction/placement pause for
inspection. Both mouse buttons stay consumed until release when leaving the camera.
Menus/focus cancel dragging; death, seating, disconnect or lost admin rights disable
the session-only mode. Existing ship-seat controls retain their previous defaults.
The shared engine-free OrbitCamera replaces the seat-only name without duplicating
orbit math. Solid target rays pull the camera inward; this is not a swept sphere.

Validation ran in an isolated worktree based on b80e804e because other chats were
editing the main checkout. The solution builds with zero warnings/errors; six new
engine-free lifecycle/math/command tests plus the existing seat-orbit test pass.
The rendered Godot UI check passes 51/51 at 1600×900. The `admin-camera` gameplay
tour passes all real-input checks: only held RMB rotates, wheel zoom wins over
noclip speed, aim stays unchanged, walking/crouching and clothing/held items render,
inventory cancels dragging, returning to FPS restores fresh-click ADS without
leaking held attacks, and server admin revocation closes/denies the camera.
Six screenshots under `docs/images/admin-self-camera` were visually inspected.

Capture limitation: normal startup disconnected during rendering stalls on three
attempts. The successful tour used only a temporary 120-second client/server
transport timeout; normal loading data and all camera code remained in use. Its
log also reports imported Substation texture decoding/GPU allocation failures.
Both timeout edits were restored before the final build and engine-free suite;
no loading, timeout, texture or generator change is part of this feature. A
headless UI check without the required 1600×900 viewport failed geometry-driven
checks; the subsequent rendered check at the documented resolution passed.
Logs and isolated test saves are in `.artifacts/admin-self-camera`.

## Coordinated repair validation resumed — 16 October, evening

The owner asked why coordination had gone idle. The coordinator resumed actual serialized
build/test/visual work rather than treating queued chats as running. The shared Debug build
passed with zero warnings/errors. Combat passed 68/68 after the reordered-fire sequence fix.
The first foundation batch found two cliff-coverage failures and one clan-base occupied-site
fixture failure; owners repaired these without removing coverage/overlap assertions. A fresh
build and foundation rerun passed 64/64 (`20261016-194953-build` / `20261016-195242-foundation`
under `.artifacts/recovery/integrated-checks`). These do not replace a complete suite run.

The gib control passed 44/44 native checks after its diagnostic read the actual RenderingServer
counts instead of cached resource metadata; 640/64/0 live fragments matched the selected caps.
The cleared capture and settings screen were inspected, and saved preferences stayed unchanged.
Both body variants passed 16 actions / 372 held-editor samples; the holo cache was refreshed,
and axe/pickaxe/mount preview images were inspected. Held-item gameplay, physical mouse and ADS
acceptance remain open. The full UI check passed 67/67, and three crafting HUD plus three
offline clan-base images passed inspection. Existing ObjectDB exit warnings remain recorded.

The fresh Maximum/day environment run failed before its first ready checkpoint: server Timeout,
then client PeerNotFound, followed by the owned-process memory stop. No world screenshots or
FPS comparison came from that run. The coordinator assigned P8 a narrow transport-liveness
repair and foliage scratch-buffer reuse, retaining normal timeouts and visual quality. Current
source ownership, resource receipts and validation limits are in
[RECOVERY.md](repair-tasks/RECOVERY.md) and the individual `repair-results` reports.

The startup repair subsequently built successfully (`20261016-203812-build`). The bounded
transport/loading/architecture run passed 15/15 (`20261016-204130-startup`): a 12-second
render/update pause disconnects the manual control but retains the background-transport
session, while both queued callbacks dispatch only on the caller after resumption. Explicit
disconnect and connected/idempotent disposal also passed. The actual built foliage dilation
kernel matched the retained original bit-for-bit in 120 cases, and four allocation checks
passed (`.artifacts/p8-startup/dilation-result-01.json`). No claim of measured peak-memory
or FPS improvement follows from the allocation-volume check. A new native Maximum world
startup remains pending sufficient available memory; the previous failed run is not a baseline.

The next coordination heartbeat reviewed and assigned P2 a one-file `GroundLayers.cs`
temporary-lifetime cleanup. It now uploads one channel at a time, releases readback Images
and containers, checks upload results and disposes partially built arrays on failure. Final
texture data, compression, mips, layer ordering, metadata and the cached resources are preserved.
An isolated native Godot 4.7.2 Forward+ check passed all 48 source/array byte and metadata
comparisons plus 48 post-cleanup GPU sampling comparisons and three positive controls. It
exited zero in 8.41 seconds with at least 5.188 GiB free; stderr was empty. Evidence is under
`.artifacts/p2-environment/layer-check-runs/layer-lifetime-r1/` and in P2's repair report.

The shared solution build then passed with zero warnings/errors (`20261016-213646-build`,
38.82 seconds, minimum 5.408 GiB available). All 124 prior checkpoint inputs and the 25-path
shared staged patch remain unchanged; `source-freeze-20261016-2139.json` adds the repaired
layer loader as input 125. No new material/grass candidate, LOD system, timeout or quality
change was installed. Isolated success does not establish old-versus-new peak-memory savings,
error-path fault-injection coverage or a successful full world. The Maximum retry retains its
8 GiB available-memory floor, and no heavy job remains active at this checkpoint.

The following heartbeat completed N11's actual settings APPLY/restart verification without
production edits. A native scratch project referenced the frozen game DLL and exercised the
real GIB AMOUNT row and APPLY handler through Godot input events, in isolated v2 userdata.
The first harness failed before settings loaded because Godot exposes an empty Assembly.Location;
its retained evidence is a diagnostic-host failure. A reviewed correction matched the loaded
module MVID to both hashed production DLL copies. The corrected scratch build passed zero
warnings/errors; APPLY passed 49/49 checks and a separate restart process passed 36/36.
MIN/MAX clamps, Escape cancellation and saved 30% surviving an unapplied 100% preview at exit
passed. Root and P8 inspected all six UI captures. Evidence is under
`.artifacts/p8-gibs/settings-acceptance-runs/{build-r2,apply-r2,restart-r2}/`.

The native jobs took 5.652 and 2.839 seconds, minimum 6.929 and 7.112 GiB available; no native
errors, guard stops or real preference changes occurred. All 125 production checkpoint inputs,
the game DLL and the 25-path staged patch remained unchanged. This closes the scoped APPLY/
restart integration check, not physical input or in-world destruction/damage/loot/sound
acceptance. Captures exposed a separate NOT APPLIED footer sentence hidden under the APPLY
button; the UI owner received a read-only review request and KNOWN_PROBLEMS records it.

The next heartbeat approved the reviewed footer-only remedy in `GroundUi.Settings.cs`.
It retains all button geometry/actions and fonts, uses the complete compact text
"NOT APPLIED: APPLY saves; Esc reverts.", and bounds the GPU description with the existing
measured Fit helper and a 16-design-pixel gap. The shared build passed zero warnings/errors
in 10.84 seconds (`20261016-222628-build`, minimum 5.745 GiB free); the isolated capture host
also built cleanly in 4.242 seconds (minimum 6.205 GiB). No implementation-mirroring test or
production diagnostic entry point was added.

The first visual capture was refused before launching at its 5.5 GiB resource preflight;
a subsequent sample was 5.277 GiB /58% commit with no heavy process. No screenshot or visual
pass is claimed. Three window/scale cases remain prepared, with actual production UI input
and isolated preference storage. The source/harness are frozen, all 125 previous inputs and
the 25 staged paths are unchanged, and the new source checkpoint has 126 inputs. Native
validation can resume without another build when resources permit; RECOVERY has exact hashes,
receipts and commands. No user applications were closed.

At the following heartbeat, available RAM recovered enough for the prepared footer captures.
Root ran the three native cases sequentially with fresh process/memory guards, reusing both
successful builds. All 116 assertions passed across 1600x900 automatic, 1280x720 automatic
and 1920x1080 with UI preference 1.25. Root inspected all nine dirty, saved and after-cancel
images: full APPLY/Escape guidance, saved text, GPU description and the four button labels
remain visible without footer overlap. The actual input handlers save 30/60/30 respectively
and restore those saved values after a subsequent preview is canceled.

Evidence is `.artifacts/admin-settings-footer/runs/{automatic900-r1,automatic720-r1,scaled1080-r1}/`
and `.artifacts/recovery/footer-visual-acceptance-20261016-2300.json`. Jobs took
4.365/4.254/4.203 seconds, minimum free RAM 5.548/5.698/5.810 GiB and maximum commit
58/56/56%; all exited zero with empty stderr and no guard stop. Real preferences, frozen
DLLs, all 126 source-checkpoint inputs and the 25-path staged patch are unchanged. This
closes the scoped footer defect. It does not establish physical-input, in-world destruction,
damage/loot/sound or Maximum-world acceptance. At 22:59 only 6.389 GiB was available with
53% commit and no heavy process, so the separate Maximum 8 GiB startup limit remains.

The 20:07 UTC coordination heartbeat resumed P6/N9 investigation through the existing Earth
chat, keeping its Earth/save source frozen. Its read-only report confirmed the levelling path:
vacating the helm clears pitch/roll, and assisted HoldLevel interprets neutral turning input as
return-to-level. Existing gunner-eye offsets already flow through the correct mounted sockets;
specific sightline obstruction remains unverified. Root selected a captured local-frame attitude
target rather than treating angular-rate damping as exact angle hold. The P6 owner is preparing
the minimal state/controller/persistence design, while P5 reviews snapshot/prediction ownership.
Only reports may change at this point; no implementation or native result is claimed. All 126
checkpoint inputs and 25 staged paths remain unchanged. At 23:09 local, RAM was 5.850 GiB free,
commit 53%, no heavy process; Maximum startup remains gated at 8 GiB.

After reviewing P6's full captured-target design and P5's networking boundary, root granted
the existing Earth chat a narrow C# source/test scope for N9. Active target state stays off
the wire only under the enforced inactive-before-awake-pilot-snapshot invariant; protocol 33
and all P5 firing paths remain unchanged. Planned ShipCodec v6 preserves the v5 prefix and
appends an active/quaternion target, with body-frame conversion, legacy migration and genuine
old-format fuel fixtures. Exact granted files and pre-edit hashes are in RECOVERY and
`.artifacts/recovery/p6-preimplementation-20261016.json`. This is source work in progress,
not a validated fix. Other source remains frozen, and no heavy or Git slot is granted.

By 00:07 local on 17 October, root had reviewed P6's twelve-file candidate and completed
serialized validation. The first build caught a missing Players namespace in a new test;
the owner made only that import correction and froze source again. The corrected shared build
passed zero warnings/errors (`20261016-235806-build`, 5.76 s, minimum 5.826 GiB, max 58% commit).
State/persistence/fuel/control/architecture passed 34/34 (`20261017-000010-attitude-state`,
2.84 s, minimum 5.844 GiB, max 60%). Network/parts/restart passed 12/12
(`20261017-000306-attitude-net`, 47.04 s, minimum 4.969 GiB, max 62%). All 19 new tests pass,
including actual two-client release/hold/takeover and exact pilot replay. Normal timeouts,
assertions, collision/fuel behavior, wire protocol, quality and camera data were preserved.

The source checkpoint now contains 138 inputs; all matched after the runs. Game DLL SHA-256 is
`3937ED307F56DE5B19C6D80AD7C52E4FCE423F9D834EE2C3B5721FE931807029` and the 25 staged paths remain
byte-identical. Evidence: `.artifacts/recovery/p6-validation-20261017.json`, the frozen P6
checkpoint, retained failed-build receipt and individual logs. No native N9 acceptance is
claimed: loaded cannon operation, rendered host/guest attitude and gunner sightlines remain
pending. No heavy job remains active; available memory 5.089 GiB / 61% commit is still below
the Maximum world's 8 GiB floor. Coordination continues through existing chats.

### 17 October, 00:41 local — roof source review and bounded preparation

The existing asset-audit chat completed P7/N10 source review. The side panels are generated
gables across both roof shapes, five grades and three authored LODs, with authoritative and
imported collision and matching fragments. Direct lateral sockets are missing; square Top
stacking already works in shared geometry and its old poses must remain. Admin and P8
released the precise source/exporter hunks for preparation, retaining their other ownership.

Admin identified a save compatibility hazard: new support links may merge separate bases
before their saved cupboards load, causing the second claim record to be rejected/deleted.
Root confirmed the call path and withheld production socket activation pending a preservation
mechanism and actual-save regression. Both bases' claims, authorization, inventories and debt
must survive. The admin chat continues read-only review; P7 has only source/test/exporter
preparation permission, with test-only socket data. Installed assets, production catalogue,
shared materials, protocol, other source and all staged changes remain frozen.

All 138 prior frozen inputs matched; the 25-path staged patch remained byte-identical.
Pre-edit hashes are in `.artifacts/recovery/p7-preimplementation-20261017.json`. Memory at
00:37 was 4.630 GiB available / 62% commit, with no heavy process. No build/test/import,
Blender or Godot slot was granted. No new chat, subagent or LOD project was started. Roof
source preparation is not a visual or runtime pass; generation, installation, shared build,
meaningful tests and normal placement/restore/physics/destruction captures remain pending.

At 00:52, root read the owner's new clarification in the admin chat: the requested roof
behavior is automatic joining when placed side by side, and the owner questioned the
expanded work. Root froze P7's source preparation and requested the minimum geometry/visual
join using existing adjacent supports. The proposed new structural support relation and
per-piece persistence/replication stamp are not authorized implementation. No generated
assets, production catalogue, save/network data or built binary changed, and no roof job
ran. Current P7 source edits are preserved for reassessment rather than silently discarded.

P7's reassessment confirmed that roofs on existing adjacent supports need geometry,
matching collision and fragment repair, without new structural sockets or save/wire changes.
Root parked all six source candidates with SHA-256 receipts under
`.artifacts/recovery/p7-paused-source-20261017/`. The five previously-clean tracked files
were restored to exact pre-grant bytes; the new helper was removed from production tools
only after its identical parked copy was verified. All 138 frozen inputs and production
building.json match again, so the partial source/art mismatch is removed. Staged admin and
other shared work were excluded. No build/test/generation/import/render was launched.

At 00:53 there were only 1.480 GiB available / 73% commit and no heavy process. No owned
job existed to stop; user applications were left running. The narrow roof asset repair and
remaining queue await the documented resource floors. No new chat, agent or LOD project
was started. P7 is updating only its report; the support-rule migration proposal is set aside.

### 17 October, 01:19 local — narrow roof tooling preparation

The 22:05 UTC heartbeat confirmed all leads idle and preserved all 138 frozen inputs and
25 staged paths. Read-only review of the parked exporter/merge sources and the current twig
building/gib JSON metadata found the expected direct nodes and material names. No candidate
code, binary mesh validation, generation or test ran; review evidence is retained in
`.artifacts/recovery/heartbeat-20261017-0106.json`.

At the 22:16 UTC follow-up, all eight leads were unchanged and available RAM was 3.720 GiB /
65% commit, with no heavy process. Root rechecked frozen sources/staged bytes and resumed
only the existing P7 chat's narrow asset-tooling preparation in building_kit.py, art/gibs/build.py
and the new GLB staging helper. It may prepare isolated synthetic preservation fixtures without
running them. Gameplay collision/sockets, production catalogue, installed GLBs/import settings,
materials and other source stay frozen; matching collision and art will integrate together.
No heavy slot, new chat/agent, structural migration or LOD work was granted. Root reviews
the prepared commands/hashes before any later execution. Preflight receipt:
`.artifacts/recovery/p7-narrow-preflight-20261017.json`. The roof fix remains unvalidated.

P7 finished and froze the three asset-tooling files and one isolated synthetic-control
source. Root reviewed the source changes and controls, checked exact hashes/whitespace and
backed them up with the report under `.artifacts/recovery/p7-tooling-checkpoint-20261017/`.
The new 142-input freeze retains all previous 138 hashes unchanged; all 25 staged paths
remain byte-identical. No control/generator/build/test/import/render ran. At 01:26 memory
was 3.962 GiB available / 63% commit, with no heavy process. The next control run and one-
grade generation calibration remain separately queued; no installed art or gameplay source
changed, and no quality/performance result is claimed. P7 is idle without further edit or job
authorization. Current checkpoint: `.artifacts/recovery/source-freeze-p7-tools-20261017.json`.

The 22:42 UTC heartbeat found a new resource failure: at 01:43 local only 0.938 GiB was
available with 88% commit; at 01:58 it remained 1.112 GiB / 87%. A normal ground game,
its server using the persistent AB Test B save and eleven reusable MSBuild nodes were
running, with no matching coordinated grant. Their original launch parents no longer
exist. Root did not stop unknown-owned processes or user apps and kept every execution
slot held. This observation does not establish who launched them or the cause of the
memory pressure.

P8's read-only launcher review identified two unrestricted build commands in each normal
launcher. A subsequent narrow source-only grant appended single-worker/no-server-reuse
flags to those four commands in Play.ps1 and play-ground.cmd. P8 checked PowerShell syntax
and byte preservation; root reviewed the exact diff, whitespace and hashes. No launcher,
build, test, import or game job ran; no measured memory/FPS benefit is claimed. All other
launcher behavior and all 142 previously frozen inputs remain unchanged. The current
144-input freeze is `.artifacts/recovery/source-freeze-launchers-20261017.json`, backed up
with the two launchers under `.artifacts/recovery/launcher-checkpoint-20261017/`. The staged
25-path patch is byte-identical. Receipt: `.artifacts/recovery/heartbeat-20261017-0142.json`.
P8 is idle with no further grant. Roof controls/generation and runtime validation remain
queued behind the unchanged workload-specific resource floors, including 8 GiB for Maximum.

At 02:16 local, no game/server/build process remained and memory had recovered to 6.759 GiB /
53% commit. Root verified all 144 frozen inputs and staged bytes, then granted the existing
P7 chat only the prepared synthetic merge controls. They completed at 02:22 with all 41
checks passing, exit 0 and empty stderr; guarded elapsed time was 0.321 s, sampled minimum
available RAM 7.130 GiB and maximum commit 51%. Root reviewed the actual result and guard
receipt. These controls cover synthetic merge preservation and refusal rules only; no real
roof asset, geometry, collision, visual or runtime acceptance is implied.

The 08:20 UTC follow-up found all prior results unchanged after the controls; available RAM
at 11:23 local was 6.625 GiB / 40% commit with no heavy process. After rechecking the 144
input hashes and 25 staged paths, root granted P7 one separate twig roof-source calibration
in fresh scratch output, guarded by the existing 5.5 GiB/80% start and 2 GiB/85% stop limits
and 600-second ceiling. Merge/fracture/import/install/other-grade work is not granted.
The user's game remains undisturbed; any new concurrent game/build means stopping only the
owned calibration. Maximum world startup still requires 8 GiB.

The single twig calibration exited 0 after 9.469 s with minimum sampled 6.933 GiB free and
maximum 37% commit; no guard stopped it. It generated six render meshes at the existing
three detail levels and two slab hulls without full-height gable geometry. P7's input
hashes and 1,187-file watched inventory remained unchanged. Root reviewed receipts/logs
and the structural report. The only stderr was a future Blender material-API deprecation.
The 1,594,840-byte scratch candidate is not installed; no merge/fracture/other grade/import/
render/gameplay check ran. All 144 frozen inputs and 25 staged paths are preserved. P7
released the slot; next scratch merge and fracture calibration require separate grants.
Evidence: `.artifacts/recovery/heartbeat-20261017-1123.json` and `docs/repair-results/P7-building.md`.

The 08:33 UTC follow-up serialized two separate P7 jobs after fresh preflights. The actual
twig scratch merge passed in 3.584 s (minimum 6.638 GiB / maximum 39% commit), preserving
112 unrelated nodes, original data/material records and all protected files. Its retained
unused roof buffers increase file size 10.758%; this is not an optimization. The separately
granted fracture calibration exited 0 in 8.338 s (minimum 6.219 GiB / maximum 41% commit),
producing two groups of nine fragments and 6,264 final exported triangles. Nothing was installed.

P7's read-only bounds check flagged square-fragment Y/Z extents 5.222/3.138 mm beyond the
original LOD2 mesh, compared with a 2 mm diagnostic tolerance that is not an approved art
acceptance rule. Coordinate conversion/transforms check out; these fragments fit LOD0/LOD1
AABBs and no full-height gables remain. The missing limited intermediate mesh prevents
attribution to simplification versus cap dressing. P7 proposed a narrow gather/limit bounds
diagnostic, with no source repair or tolerance relaxation yet justified. P8 independently
confirmed the existing 1000 target is pre-fracture/soft; nine fragments remain and final
triangle counts are below installed roofs, while square vertex entries rise slightly.
No performance improvement is claimed and no budget/quality change is justified solely
to force a 1000 exported count. Both read-only reviews are recorded in their owner reports.

Both execution slots are released; all eight leads are idle/notLoaded and no edit/job grant
remains. All 144 frozen inputs and staged 25-path bytes remain preserved. At 11:46 only
5.391 GiB was available / 45% commit, so the next general/native/generation diagnostic is
held at its existing 5.5 GiB floor. Maximum remains 8 GiB. Evidence:
`.artifacts/recovery/heartbeat-20261017-1134.json`. No unchanged run was retried.

The 08:49 UTC follow-up used a source-only P7 grant while generation memory was below
its 5.5 GiB floor. P7 prepared a frozen isolated square-roof diagnostic that uses the
unchanged original gather/limit functions and records coordinates/counts/bounds before
and after each stage, stopping before fracture/export/material writes. Root reviewed
its exact-hash/AST loading safeguards and imported shared module setup. No Python parse,
Blender execution, build, test or production write occurred; runtime validation remains
pending. The existing 144 inputs and 25 staged paths are unchanged. At 11:54 free RAM
was 4.743 GiB / 49% commit, no heavy job. P7 is idle; there is no execution/edit grant.
Evidence and separate prepared-driver hash: `.artifacts/recovery/heartbeat-20261017-1149.json`.
The next run needs a fresh guarded grant; the 8 GiB Maximum startup floor is unchanged.

The 09:10 UTC follow-up completed two separate serialized P7 jobs after fresh resource/
process checks. The square gather/limit diagnostic passed in 8.329 s (minimum 5.837 GiB,
maximum 45% commit); gathering preserves bounds, while the existing limiter introduces
both earlier extent changes before fracture. Final fragment vertices match within
0.00033 mm. No simplifier, cap, transform or tolerance change was made. The diagnostic
stops before fracture/export and does not replace normal visual acceptance.

The separate actual twig scratch gib merge passed in 1.227 s (minimum 6.577 GiB, maximum
43% commit). All 18 roof fragments retain geometry/transforms; 18 unrelated groups / 180
nodes and original records/data/materials remain unchanged. The 4,956,292-byte pack grows
9.10% from retained obsolete data, with no performance claim. Root reviewed both receipts,
logs, coordinate/transform evidence and preservation inspections. No asset was installed.
All 144 frozen inputs, 63 protected production files and 25 staged paths are preserved.
Both slots are released, P7 idle. Other grades, matching minimal authoritative collision
and native visual/placement/fracture checks remain pending. No broader structural system
was authorized. Final 12:20 resources: 6.333 GiB / 44% commit, no heavy process; Maximum
startup still needs 8 GiB. Evidence: `.artifacts/recovery/heartbeat-20261017-1210.json`.

The 09:24 UTC follow-up granted existing P7 one bounded serial slot for the four remaining
roof grades after confirming 6.055 GiB / 45% commit and no competing heavy job. Wood, stone,
metal and armoured each completed source generation, building merge, fragment generation
and gib merge: 16/16 exits and structural/provenance/preservation inspections passed.
The slot took 248.049 s including inspections; guarded process time totaled 87.733 s.
Across 91 samples, free memory stayed at least 5.868 GiB and commit at most 46%, with no
retry, overlap or guard stop. Only the existing source-export material deprecation appeared.
Root reviewed all stage receipts/inspections/logs and independently rehashed their outputs.

Together with twig, all five roof/debris scratch pairs are ready for the integration step;
full-height panels are absent, existing LODs/recipes/seeds/budgets retained, and all unrelated
content preserved. Retained obsolete data increases file sizes; no performance improvement
is claimed. All 144 frozen inputs, 63 protected production files and 25 staged paths remain
unchanged. No installed asset, collision source, original project or library changed.
P7 released its slot. Next are minimal matching authoritative collision, coordinated asset
integration, build/focused tests/imports and actual visual/placement/fracture acceptance.
The root read-only source audit found Gables still added by PieceShapes.For; no replacement
socket/support/save/network system is needed. Final 12:35 resources: 7.074 GiB / 42% commit,
no heavy process. Maximum startup still requires 8 GiB. Root evidence:
`.artifacts/recovery/heartbeat-20261017-1224.json`; detailed per-stage summary:
`.artifacts/p7-roofs/guard-armoured-gibs-merge-r1/batch-summary.json`.

The 09:42 UTC follow-up integrated all ten inspected roof/debris packs and removed only
the authoritative gable collision in PieceShapes. The private Roof method is byte-identical;
existing footprints, Top poses, placement/support/catalogue, wire and saves are unchanged.
Backups and before/promotion/after manifests are in `.artifacts/p7-roofs/integration-r1/`.
Exactly ten of 63 protected files changed; the other 53, import/material settings and all
144 prior frozen inputs remained unchanged. Four meaningful RoofPanelTests were added.
The new immutable freeze `source-freeze-roofs-20261017.json` records 156 inputs.

The single solution build reported success, zero warnings/errors, 37.55 s. Its wrapper
then failed serializing integer PID keys, losing final exit/resource fields. Preserve the
original running receipt and separate post-run inspection; compiler exit and full resource
coverage are unknown. No rebuild was performed. A separately granted test run passed five
architecture checks and the 298-mesh building-fit check before stopping at the first building
network test. A StrictMode ordered-dictionary aggregation error masked the primary cause
and exit; only three durable samples survived (minimum observed 7.909 GiB, maximum 39%).
No test failure was established. That incomplete run was not repeated unchanged.

P7 prepared a new scratch guard with durable primary errors, independent secondary failures,
numeric sample aggregation and shared-file stdout reading. Seven lightweight fixture groups
passed. A live-writer fixture reproduced the old reader's sharing violation, a possible
earlier cause rather than proof of the lost error. Root reviewed the implementation and
authorized separate roof-only and remaining-regression runs against the same compiled DLL.
The four roof tests passed in 0.5 s, exit 0, no guard errors/stops, minimum 8.122 GiB / maximum
39% commit. The remaining 20 building/network/persistence/gib tests passed in 15.9 s, exit 0,
no guard errors/stops, minimum 6.969 GiB / maximum 42%. Both preserved 228 recorded inputs.
Root reviewed logs, receipts, tests, guard diff/fixtures, installed hashes and preservation.
This is focused validation, not a completed full suite or visual acceptance.

P7 is idle and all slots are released. The staged 25-path patch remains byte-identical;
all 156 frozen inputs match. No original-project/library edit, Godot import/render, hosted
playthrough, new LOD project or broader structural roof change occurred. Next are serialized
incremental imports and actual visual/placement/walk/shot/fracture checks. Maximum startup
still requires 8 GiB; no FPS gain is claimed. Keep follow-up active. Root evidence:
`.artifacts/recovery/heartbeat-20261017-1242.json`; detailed results are in P7's report.

The 10:43 UTC follow-up reviewed unchanged compact statuses for all eight chats, the 156
frozen inputs and staged 25-path checkpoint, then granted P7 one incremental Godot import.
Its fresh guard copy retained robust outcome/resource recording and the established
60-second import limit; all seven lightweight fixture groups passed before launch.
Godot 4.7.2 .NET imported exactly the ten changed building/debris packs in 11.902 s, exit 0,
with no warning/error, guard error, resource/competing stop or retry. Launch resources were
7.761 GiB / 39%; eleven samples show minimum 6.279 GiB / maximum 45% commit.

Root reviewed the exact guard diff, logs, receipt and cache inspection. All ten source/cache
MD5 pairs match current GLBs; destination checksums match the new scene bytes. Only ten
scene caches and ten MD5 records changed among 3,498 imported files. No cache was added or
removed. All ten UIDs, 1,723 import-settings files, 156 frozen inputs, 228 guard inputs and
the staged patch are unchanged. No production source/art edit, forced import, build, render
or hosted game ran. P7's report and REPAIR_BUILDING validation paragraph record the result.
Its slot is released and it is idle; actual visual/placement/walk/shot/fracture acceptance
remains pending. Full Maximum startup still requires 8 GiB; final 13:59 observation was
6.296 GiB / 47%, no heavy process. Existing BuildSandbox uses plain collision-tile ground;
its bounded capture can be reviewed separately from full-world acceptance. ChildServer.Find
selects the newest Debug/Release binary, to be checked before any hosted validation.
Evidence: `.artifacts/recovery/heartbeat-20261017-1343.json`; keep follow-up active.

The 11:07 UTC follow-up reviewed all eight unchanged compact chat statuses, current source/
index hashes and 7.093 GiB / 42% initial headroom with no heavy process. Root inspected the
existing building sandbox: four metal square roofs, plain collision-tile ground, fixed-camera
capture and readiness gating. P7 received one bounded 1600x900 Maximum/LOD0 view of the
open gable using the existing built client, without source changes or a new renderer/adapter.
The 5.5 GiB native floor and full-world 8 GiB requirement were kept distinct and unchanged.

P7 prepared a fresh guard, pinned 19 client/dependency DLLs and imported resources plus
source/configuration/index evidence, and passed seven lightweight guard fixtures. Its
actual launch preflight was only 5.493 GiB / 49% commit, so the guard refused before Godot
started. No native PID/exit, PNG, stdout/stderr, stopped process or secondary guard error
exists. The second sample was 5.484 GiB; later root observation was 5.251 GiB / 51% with
no heavy process. No retry or resource-floor change followed. All 1,989 recorded inputs,
including user graphics settings, 156 frozen inputs and staged 25-path bytes are intact.
Root reviewed the exact prepared scope and retained refusal. There is no visual finding
from this turn. P7 released the slot and is idle; the unexecuted capture awaits fresh
headroom and a separate grant/evidence label. Tests and imported assets remain validated;
visual/gameplay/fracture acceptance remains open. Evidence:
`.artifacts/recovery/heartbeat-20261017-1407.json`; keep follow-up active.

The 11:27 UTC follow-up found recovered headroom, unchanged compact statuses for eight
chats, all 156 frozen inputs and the staged 25-path patch intact. P7 received one fresh
guarded attempt of the unexecuted metal-square roof capture, preserving the earlier refusal.
The native Vulkan Forward+ capture saved a 1600x900 image with ready=True at 2.01 s and
exit 0. Guarded time was 7.932 s; eight samples show minimum 5.045 GiB / maximum 53% commit
after a 6.312 GiB / 47% preflight. Root reviewed the exact output-path-only guard change,
seven passing fixture groups, logs, receipt and actual image. All 2,034 recorded inputs,
including the frozen source, DLLs, imported resources, user settings and index, still match.

The front gable is visibly open; the same view shows an apex notch and bright slope slits.
P7's light geometry diagnosis established that the apex deck coordinates are unchanged and
a representative backing slit exists in both old and current repaired roofs. The raw face
is falsely subtracted by an inherited coplanar-repair bug: rounded-identical plane groups
mix polygons projected onto different axes. A deterministic shared basis and actual failing
pair/coverage regression are the next narrow repair step, before any asset regeneration.
No geometry, source, build or import changed during this follow-up.

Godot also emitted a 30-instance ObjectDB shutdown warning. P8's read-only triage found the
same count in an older disconnect run, plus two source lifetime candidates, without assigning
causality or FPS impact. One separately granted verbose diagnostic is proposed; none ran.
Both owners completed and released their slots. Other six chats remain unchanged. Full
Maximum world startup retains its 8 GiB requirement; the 14:55 observation was 6.713 GiB /
45% commit, with no heavy process. Roof image evidence is limited to this metal LOD0 view;
all-grade/shape/LOD and normal hosted placement/walking/shot/fracture acceptance remain open.
Evidence: `.artifacts/recovery/heartbeat-20261017-1427.json`, P7/P8 reports and
`.artifacts/p7-roofs/native-r2/`. Keep follow-up active; no original project or user apps changed.

The 12:03 UTC follow-up inspected all eight unchanged compact worker statuses and found
6.880 GiB available / 44% commit with no heavy process. P7 received the narrow coplanar
projection-tool correction and one bounded regression grant. The tool now selects the
dominant axis from the rounded plane key, giving every grouped face the same basis.
Its old bytes are preserved; no runtime C#, installed asset or import setting changed.

The first guard refused before launching because its added broad Python-name check matched
persistent MCP/catalog helpers. Root inspected complete executable/command lines and pinned
six exact service tuples. A fresh guard uses that exact classifier at preflight and runtime;
unknown Python commands or missing metadata still block, as do native game/build jobs.
All eight guard fixture groups passed, including PID-reuse/owned-identity cases. The original
refusal and 45 evidence files remain unchanged; no helper or user app was stopped.

The single actual regression run passed all five groups, exit 0 in 1.445 s, empty stderr and
no guard errors/stops. It made 36 tiny calls through the actual repair function, never a full
asset pack. Retained old behavior removes the backing at the observed raw-triangle pair;
the correction preserves it. Order, cyclic/winding variants, true overlap union, dominant
axis ties, interpolated UVs/normals/colors/tangents, metadata and byte determinism passed.
Three resource samples show minimum 7.662 GiB / maximum 43% commit. Root reviewed code,
test/guard changes and results, and rehashed all 2,137 protected inputs with zero drift.

Checkpoint `.artifacts/recovery/source-freeze-roof-clip-20261017.json` retains all prior 156
inputs and adds the validated tool and regression, for 158. The staged 25 paths remain
byte-identical. P7 released its slot; all other chats are unchanged. Installed roofs still
need scratch full-candidate repair, geometry/coverage checks and native validation; no game
visual fix or N10 completion is claimed yet. Full Maximum startup still requires fresh
8 GiB headroom. Evidence: `.artifacts/p7-roofs/coplanar-r2/` and root
`.artifacts/recovery/heartbeat-20261017-1503.json`. Keep the existing follow-up active.

The 12:36 UTC follow-up found 9.192 GiB available / 37% commit, unchanged compact worker
statuses and no heavy process. The root granted P2 one Maximum/day native route after the
already validated transport, foliage-dilation and terrain-array lifetime repairs. The fresh
r4 guard retained normal timeouts, the original 480-second bound, unchanged quality, exact
helper classification, owned PID/start identities and 8 GiB/80% launch / 2 GiB/85% stop
limits. Eight lightweight guard fixture groups passed. No source/build/import/candidate
installation, agent creation or user-process termination occurred.

The actual in-process hosted route completed: six stops over 628.031 m, 18 ready 1600x900
normal/root/elevated captures, 445 trajectory samples, technicalChecksPassed=true and
visualQualityAccepted=false. Native exit 0 after 350.475 seconds; route clock 339.952 s.
Preflight was 8.976 GiB / 38%; 267 samples show minimum 3.3355 GiB / maximum 75% commit.
There was no guard stop/error. Root rehashed all 6,275 protected input paths and 24 evidence
outputs with zero drift; the 158-source freeze and staged 25-path patch are preserved.
No native heavy process remained, and P2 released its slot.

Root reviewed all six normal captures and woodland roots. Terrain bands, clipped-looking
grass rows, sparse/jagged trees, patterned foam, a stepped distant water edge and a hard
scalloped wellhead ground-patch border remain visible. Six stationary frame means range
24.93–30.20 ms (about 33–40 FPS); mean GPU time is 24.36–29.63 ms. These are baseline
measurements, not proof of a quality-preserving FPS gain. The shared-position water/cliff
walking leg contains no frames and its zeros are not a performance result. The elevated
captures use the explicit 9 m diagnostic lift. P2's visual candidates remain staged; night,
broader-biome, cliff and visual acceptance remain open.

The run retained five clock-correction notices but no timeout/lost connection before normal
shutdown. stderr reports seven leaked Texture RIDs and two ObjectDB instances. Root routed
that evidence to P8 for read-only triage, distinct from the roof viewer's 30-instance warning;
no verbose rerun or production lifetime change is granted. Normal child-server startup,
prolonged gameplay, full-suite validation and measured old-versus-new memory/FPS improvements
remain outstanding. Preserve the 8 GiB Maximum world gate. P7 candidate generation and P8
verbose capture remain queued, with all heavy slots released.

Evidence: docs/images/repairs/p2-environment/baseline-max-day-r4/environment-route.json
(SHA-256 E3EC122D86A5BEA8515B44CB506F0FBB9D5E18447AF011E90719CB1014331A64),
.artifacts/p2-environment/baseline-max-day-r4/run/receipt.json
(5D90A0F10777BECDB4661012EFABBAEAB53FEAC731EC1CF91D0E1F678CF98C5D), P2's report,
and .artifacts/recovery/world-route-r4-root-review-20261017.json. Follow-up remains active;
remaining work includes implementation and technical validation, not only user acceptance.

P8 completed the read-only r4 triage and returned idle. Root verified that EnvironmentRoute
already disposes each captured Image with using; the Main.Capture lifetime suspect belongs
to the separate viewer and is not an explanation for r4. The seven-Texture/two-ObjectDB
signature resembles older world logs only by counts/types. Class/owner attribution remains
unknown. Retain separate future diagnostics and do not start an unchanged route retry.

The 13:18 UTC follow-up reviewed all eight unchanged compact chat statuses, found 8.74 GiB
available / 39% commit without a native heavy job, and verified the 158 frozen inputs and
25-path staged patch. P7 received one scratch-only metal roof repair/inspection slot using
the already validated projection correction. No production source or asset was changed.

The fresh guard retained exact helper classification, owned PID/start identities, durable
error handling, 5.5 GiB/80% launch and 2 GiB/85% stop thresholds. Eight fixture groups passed
with the granted 120-second bound. The single Python process repaired the preserved raw
six-mesh/two-hull subset once and inspected it, exiting 0 after 5.461 seconds. The repair
itself took 1.247 s. Six resource samples reached minimum 8.737 GiB / maximum 39% commit
from an 8.989 GiB / 38% preflight. Stderr was empty; no guard error/stop or process kill.

The diagnosed backing point is restored. All 4,608 deterministic raw-surface probes are
covered; three old missing samples are present now and no old-covered sample was lost.
Materials, node/primitive metadata, transforms and both hulls match exactly; valid finite
attributes/indices and unchanged live bounds were recorded for both shapes at all three
existing LODs. Root reviewed the actual inspection code/results, guard diff and logs, and
independently rehashed all 6,295 protected inputs with zero drift. Shared staged work,
source/assets/imports/DLLs/settings, prior evidence and original/library files are preserved.

The candidate remains uninstalled. There are 93 sampled multiple-interior locations within
10-micrometre plane tolerance, versus old111/raw180; square LOD0 has12 versus old11. Three
near-zero-area triangles have matching per-node counts in old geometry, without proof that
the faces are identical. No extra pruning or repair occurred. These are discrete samples,
not exhaustive surface-union or watertightness proof, and no native visual result exists.
Next is focused provenance/intersection review before merge/fracture/native acceptance;
other grades, gameplay and N10 remain open. No general mesh/LOD project is implied.

P7 released its slot and is idle. Other seven chats are unchanged. No Blender, build,
import, render, new agent or user/helper termination occurred. The Maximum world gate
remains 8 GiB; no new world route was run. Evidence: .artifacts/p7-roofs/metal-clip-r1/,
docs/repair-results/P7-building.md and .artifacts/recovery/metal-candidate-root-review-20261017.json.
Candidate SHA-256: 2BD9275628251C9A8C27195B8E2DAC91A7090639B9844876CEC71F39BF33E8F0.
Keep follow-up active for the remaining implementation and technical validation queue.

The 13:43 UTC follow-up found 8.779 GiB / 39% commit, 24 exact approved Python helpers,
no competing heavy job and unchanged compact worker statuses. The 158-input freeze and
25-path staged patch matched. P7 received one read-only residual geometry diagnosis;
P2 separately reviewed retained visual candidates without a heavy slot. Both returned idle.

P7's sole Python diagnosis exited 0 after 5.444 s under a 60-second guard, with no repair
calls, stderr, guard errors or stops. Eight adapted fixture groups passed. Preflight was
9.034 GiB / 39%; six samples reached minimum 8.804 GiB / maximum 40%. Root reviewed the
runner's integer/rational plane and projected intersection calculations, guard changes and
provenance limits, then independently rehashed all 6,347 protected entries with zero drift.
The 93 sampled locations resolve to 92 near-coincident pairs: all projected intersections
are positive but none is exactly parallel/coplanar. All have local raw-surface equivalents
(80 exact raw face pairs), and 90 have local old-surface equivalents. The two additional
square-LOD0 samples are restored raw backing removed by the faulty old repair. All three
degenerates are exactly zero-area and match old geometry and full attributes. These results
justify no further geometry correction before controlled engine visual review; exhaustive
coverage and absence of z-fighting remain unproven. No candidate installation occurred.

P2 prepared a two-file scratch patch for GrassField.cs and surface_grass.gdshader. UV2
separates authored lean/curl from root footprint; the shader scales growth and bend with
live height, uses sampled triangle-plane upright roots, reduces burial to 5 mm and tapers
the analytic silhouette. The existing height multiplier, litter placement, cards, density,
radii, LOD and material settings remain unchanged. Root reviewed the complete patch and
production context, verified all 206 protected inputs and five outputs, and ran only
git apply --check (exit 0). The patch is not applied, compiled, tested or rendered. Native
mesh-array/UV2 and actual shader checks, visual comparison and GPU timings remain needed;
more visible pixels and vertex work may increase cost. Authored-cover, terrain and water
proposals are deferred; removing alpha-to-coverage is excluded under quality preservation.
Identical route settings do not ensure identical camera/character poses or shader time;
strict paired-image acceptance requires separately reviewed replay or remains open.

Evidence: .artifacts/p7-roofs/metal-residual-r1/ and root metal-residual-root-review-20261017.json;
.artifacts/p2-environment/candidate-review-r1/ and root grass-candidate-root-review-20261017.json
(both root reviews under .artifacts/recovery/). No source/asset/import/binary/preference or
staged changes, build/import/render, new chat/agent or user/helper termination occurred.
All heavy slots are released. The 8 GiB Maximum world gate and active follow-up remain;
grass implementation, roof visual validation and P8 attribution require subsequent grants.

Closing inspection at 17:16–17:19 local identified independently user-started work in
"Fix ship controls in TPS view" (01a14a36-763b-7632-9783-dac4f69cbac8). Its GroundView.cs
changes now differ from the retained 158-input freeze and its build replaced shared binaries.
The 206-input P2 review above passed at 17:14:31, before that later source change. Preserve
the new work and old evidence; do not silently rebaseline or treat prior binary receipts as
current validation. The new owner's Low-preset hosted diagnostic stopped at its own 1,928 MiB
memory floor before the controls check; that chat is handling the failure. Root started no
competing job and stopped no process. At 17:18:49, 7.252 GiB / 44% commit remained, with
32 exact helpers and no heavy process. Further coordinated launches are held while that
work is active, pending source/binary reconciliation. Shared staged bytes still match.

## Ship TPS input separation — 17 October 2026

The owner reported cockpit controls operating in TPS. Source inspection found two causes:
`GroundView.SeatInput` let a pilot's non-RMB mouse motion fall through to cockpit steering,
and the persistent cursor/smoothed turn survived camera changes. All seated third-person
mouse motion now stays in the orbit route. V clears both cursor and output; the TPS
`ShipControls.Read` call also clears mouse steering before combining keyboard input.
Arrow keys still pitch/yaw, RMB drag orbits, the wheel zooms, and throttle/assist remain
unchanged by switching views. The TPS HUD names those controls. The legacy ship-centred
`--view ship` development sandbox retains its existing separate chase controls.

The `repair-ships` hosted tour now seeds a real cockpit turn before V and checks outgoing
ship commands for neutral TPS steering under unbuttoned/LMB/RMB motion, arrows in both
directions and release, and neutral cockpit return followed by fresh mouse steering. Its
existing throttle, airborne exit, unattended flight and station-button checks remain.

Validation:
- The complete solution built with zero warnings/errors. Receipt:
  `.artifacts/recovery/integrated-checks/20261017-171627-build.receipt.json`.
- All 33 selected ShipRepair, ShipThrottle, LocalFlight, AttackStation, AdminCamera and
  Architecture checks passed; `.artifacts/ship-tps/tests.stdout.log` and `tests.receipt.json`.
- The first real hosted tour used a fresh isolated save and a temporary Low preset. The
  memory guard stopped it during world loading at 1,928 MiB available after 34.72 seconds,
  before the input assertions. This is not a passed hosted gameplay run. Its logs and
  receipt remain in `.artifacts/ship-tps/hosted-r1.*`; no saved graphics setting changed.
- A small native Godot scene referenced the exact built production client DLL and invoked
  its actual SeatInput/ReadInput methods with native held-key/button state and a seeded
  pilot module. All 27 assertions passed: cockpit steer/centre, V clearing, preserved
  throttle/assist, unbuttoned/LMB/RMB TPS motion, independent button releases, orbit,
  zoom, arrow steering/release, stale cursor suppression, throttle and cockpit return.
  This deliberately omitted terrain, simulation and a network connection; it does not
  substitute for a complete hosted flight. Receipt/logs:
  `.artifacts/ship-tps/input-check-native-r1.*`. Native run stderr was empty, exit 0;
  minimum available memory 7,237 MiB. The scratch harness's initial enum compile error
  and corrected build/import receipts are retained in the same folder.
- Actual production ShipHud captures were visually checked at 1600 × 900, on a plainly
  labelled fixture backdrop: [cockpit](images/ship-tps-input/cockpit-hints.png) and
  [TPS](images/ship-tps-input/third-person-hints.png). Hints are legible and unclipped.

Production client DLL SHA-256 for the successful native input check:
`0C502CB0E0084103782F0BD4A4FE6808EC8C157FF9A6297C1B86CEE53F75E61A`.
The full-world tour remains pending sufficient memory headroom. No terrain, physics,
network protocol, flight model, generated content or original-project files were changed
by this fix.

## Grass integration and guarded build refusal — 17 October 2026

The 14:22 UTC follow-up read the recovery/backlog and compact worker statuses. The independent
ship-controls owner was idle. Root inspected its source changes and build/test/native fixture
logs; production client SHA matched the fixture's copy. Its 33 focused checks and 27 isolated
native input/HUD assertions do not establish a hosted flight or Maximum world pass. Root
created the explicit 163-input ship/grass checkpoint, preserving 157 prior inputs, updating
GroundView.cs and adding five related source paths. Shared staged bytes were preserved.

With 6.709 GiB / 46% and no competing job, P2 received the exact reviewed two-file grass
integration plus one guarded build and conditional focused tests. The patch applied with
exact candidate hashes. Its preparation then stopped because forward/backslash aliases
retained inconsistent expected hashes after the two authorized changes. Root independently
rehash-checked all 12,623 entries as 8,821 canonical file identities: only those two sources
changed. A separately granted scratch-only correction used absolute Windows paths and
OrdinalIgnoreCase, rejected conflicting aliases and updated only the two approved source
expectations. The failed preparation and all original before-images were retained; the
source patch was never reapplied. The unchanged build/test guards' eight fixture groups
were reused after hash checks.

The actual build preflight at 14:43:37 UTC refused launch at 5.0365 GiB available / 51%
commit, below the unchanged 5.5 GiB start gate, with no competing job. No compiler, test,
import or renderer launched. Wrapper exit 1 is not a compiler failure; compiler exit is null.
Two preflight/final samples reached minimum 5.0140 GiB / maximum 51%, with no secondary
errors or process termination. P2 released its slot without retry. Root verified all 8,832
protected inputs, 896 build-output files and evidence hashes with zero drift. The client
remains the ship-controls build, SHA 0C502CB0E0084103782F0BD4A4FE6808EC8C157FF9A6297C1B86CEE53F75E61A;
it does not contain the new grass UV2 producer. The test assembly is also unchanged.

Only GrassField.cs and surface_grass.gdshader were integrated. Density, cards, radii, LOD,
graphics settings, litter and height multiplier stay as reviewed. Compiler, shader, native
root/silhouette/wind/trample checks and measured GPU/visual acceptance remain pending.
The new source-freeze-grass-pending-20261017.json preserves 161 entries and explicitly updates
two (163 total), with unbuilt status. Root's first review command had a PowerShell path-set
comparison error after successful hash loops; its note is retained and the corrected review
completed. This caused no production changes or build attempt.

Evidence: .artifacts/p2-environment/grass-build-r1/handoff-r2.json, refused run/receipt.json,
the P2 report, and .artifacts/recovery/grass-build-refusal-root-review-20261017.json. All slots
are released. The next first-build attempt requires fresh evidence and recovered >=5.5 GiB
headroom; Maximum world startup remains >=8 GiB. No quality reduction, original-project
write or user-app termination occurred. The coordinated queue and follow-up remain active.

At 14:54 UTC, fresh resources recovered to 6.846 GiB / 45%, with 26 exact helpers and no
heavy process. Root therefore granted one fresh build and conditional tests, preserving the
refusal and source patch. P2 copied the guards byte-identically and recorded reuse of their
existing eight-group fixture proofs; no fixture rerun or guard behavior change occurred.
Fresh canonical manifests checked source/assets/cache/preferences, prior evidence, index
and build-output inventories. No production source was reapplied or edited.

The solution build passed exit 0, zero warnings/errors, in 38.184 s guarded / 37.19 s MSBuild.
Preflight was 6.8183 GiB / 45%; 29 samples reached minimum 5.9982 GiB / maximum 48% commit.
The conditional no-build process passed 7/7 (five Architecture, two EnvironmentRoute),
exit 0 in 2.950 s guarded / 1.6 s tests. Four samples had minimum 6.4408 GiB / maximum 46%.
Both stderr files were empty, with no guard stop, primary/secondary error or process kill.
Receipt and finalization records match. Root independently rehashed all 9,760 test inputs,
including all 8,854 build-protected inputs and all 896 output files, with zero unexpected
drift. The build changed 265 output hashes without inventory changes; tests changed none.

The grass UV2 producer is now compiled in client SHA
8C8B4529DE908ACB29F515154564C68099BEAAB1E4C85020C377AF989D635234, MVID
356e7687-a76d-49c3-8293-781d0f5db768. The test DLL SHA is
608CD78F980C75E0B9AAC9206D7887F44BCDB3349733AA131FF6099D665F5850. The successor
source-freeze-grass-built-20261017.json preserves the same 163 sources/assets and records
the compiled binding. Staged 25-path bytes remain unchanged. P2 released the slot and is idle.

Evidence: .artifacts/p2-environment/grass-build-r2/handoff.json and build/test run receipts,
plus .artifacts/recovery/grass-build-root-review-20261017.json. No import, native render or
shader compilation occurred. The route tests establish sampled heightfield feasibility;
they do not establish hosted collisions, rendered grass quality, same-pose acceptance or
GPU cost. Those remain separate native/visual steps, with Maximum startup still requiring
8 GiB. Prior failed preparation and refused preflight remain retained, and follow-up stays active.

## Held-item admire — 17 October 2026

Requested by the owner: press N to admire/inspect the held item. Added the non-admin,
client-only `admire` command to the existing physical-key binding system, with N as its
default. Existing saved N overrides win. `bind J admire` and `unbind N` persist, including
an explicit default-removal marker so restarting cannot silently restore an unbound N.
Ordinary key presses do not write console history, HUD notices or network intents.

All 41 held models now declare a 2.8-second admire curve in held-presentations.json.
The shared FPS renderer applies inspection over the actual idle, including imported
skeletal idles. It transforms the existing wrist/item relationship, keeps the shoulders
fixed, limits both holding hands to their reach and solves the arms. The existing IK
keeps its default wrist-twist behavior; this additive action preserves base forearm twist.
Endpoints are exactly the idle pose. Nothing is consumed, reloaded, fired or damaged by
admire. Repeated key presses do not restart it. Attacking, aiming, reloading, switching
slots/items, menus, lost focus, free look, sprinting, swimming, seats and death/wounding
cancel or refuse it; gameplay actions have priority. This is local FPS presentation.

The first candidate moved the complete rig with the item. Although its transform tests
passed, screenshots exposed the cut ends of the FPS forearms on rifles and a source-bound
hatchet. Replaced that approach with rooted arm solving, strengthened the native checks,
and recaptured rock, torch, rifle, bow and metal-hatchet contact sheets. The final sheets
are in docs/images/admire and were inspected directly, not just checked for file existence.

Validation: solution build passed with zero warnings/errors; 17 focused tests
passed (binding persistence, content coverage, held presentations, architecture and admin
input). Final rooted native checks passed on both bodies: 41 items × 6 phases × 2 bodies
= 492 samples, including the three skeletal-idle tools. They check finite transforms,
rooted shoulders, primary/support wrist attachment, exact idle endpoints, playback,
automatic completion, cancellation, normal action interruption and changing held items.
Commands: `--view held-editor --admire-check [--body female]`.

The initial full test run was stopped when the user's existing game and tests left under
1 GiB available RAM. After the owner closed the game, the resumed full run passed 238
checks but grew to over 6 GiB itself. Its completed checks are retained; the remaining
264 were run in fresh bounded processes to prevent terrain-cache accumulation.
Evidence and the exact 502-test manifest are under .artifacts/admire. A dedicated hosted
input tour (`--tour-steps admire`) uses its own save and port, with a per-run Medium preset
for input validation; it does not change the user's saved graphics settings or game save.
Final aggregate: all 502 unique engine-free tests passed, with no failures. The manifest
was independently audited against all public static test methods. Actual completed test
names were reconciled against it; an initial helper-class discovery mismatch was corrected
and its 21 omitted tests passed in two final batches. Zero-match batches were not counted.
All completed batches exited 0. Hashed logs, the exact passed-name list and empty
missing/unexpected/failure lists are recorded in .artifacts/admire/test-results.json.

The isolated hosted input tour passed, exit 0, using real N, mouse, R, Alt and belt-key
events. It verified completion without restarting, unchanged inventory/ammo/condition,
attack/aim/reload priority, refusal during reload, inventory/menu/free-look gates, switching
to the starting rock, holstering and empty hands. Live rifle and rock screenshots were
saved beside the contact sheets and inspected with the ground UI visible. The first tour
attempt exposed a fixture race: it captured ammo before the authoritative reserve update
arrived after reload. Waiting for that update fixed the fixture; production code was not
changed for this failure. Both attempts and their separate saves/logs are retained under
.artifacts/admire. The successful tour still logs transparent-viewport SSS fallback and
seven texture/two ObjectDB shutdown warnings, matching already documented cleanup issues;
this work does not claim to fix those warnings.

The workshop continues to report one ObjectDB instance at exit (exit code 0); no claim is
made that this warning is caused or fixed by admire. No original-project files or saves
were accessed. Existing staged and unstaged work was preserved. These changes are left
uncommitted because the shared files depend on pre-existing staged admin-camera work;
committing the current index or whole paths would absorb unrelated changes.

## Tool and world video reference study — 17 October 2026

The owner accepted the metal hatchet and metal pickaxe holding poses in two supplied
screenshots, rejected other tool angles/positions, and requested close study of Enurfase's
41-second chopping clip and Facepunch's Rust trailer. The written result is
[REFERENCE_STUDY_TOOLS_WORLD.md](REFERENCE_STUDY_TOOLS_WORLD.md), with three chronological
trailer ledgers, accepted local screenshot baselines, a code audit and proposed acceptance
sequence. DESIGN.md ART-03 and HELD_ITEMS.md record the new acceptance scope without
claiming broader animation acceptance or replacing the confirmed ground UI/art direction.

Visual coverage: 1,233 native-30-fps frames plus 9,229 native-60-fps frames, all 106 consecutive
contact sheets reviewed without temporal sampling gaps. Sheets use 192x108 thumbnails;
28 selected trailer frames were additionally examined at 1280x720 and 36 consecutive axe
frames at 480x270. This is not full-resolution scrutiny of every pixel. The temporary
review copies/frames remain outside the repository at
C:/Users/uruskan/AppData/Local/Temp/hollow-video-study; no reference-game assets were admitted.

Audio remains a real limitation: the available interface explicitly returned 'audio content
omitted because you do not support audio input' when presented with a five-second extracted
segment. Captions support the speaker's near-fall-creak/noise-exposure advice; code review
establishes current swish/contact routing. Neither is a listening test or an audible-sync
pass. The study marks audio proposals and the remaining listening work explicitly.

The audit confirmed that source-bound tools bypass procedural gait/sway/sprint composition;
stone pickaxe already shares the metal tools' motion but has a shorter model; stone and
salvaged axes remain on legacy item/IK curves. Swish plays at action start; natural impact
sound is chosen through target gather cues. No gameplay source, profile, asset, timing,
terrain or save was changed by this research task. No build, engine test, Godot capture or
runtime acceptance is claimed. Initial runtime work was unsuitable at 0.82 GiB available
with two existing Godot instances; subsequent active validation belonged to other work.
Only lightweight analysis and documentation were performed here. Existing staged and
unstaged implementation work was preserved; this documentation is left uncommitted with
its shared design/development context rather than committing unrelated work.

## Bow aim and camera clearance — 17 October 2026

The owner reported the drawn bow pointing left and looking broken. Reproduced the exact
pose in the held-item workshop before editing: the existing aim profile applied 27° yaw,
-38° roll and a 20 cm camera-ward shift. The actual arrow pointed 26–28° away from camera
forward. The GLB arrow axis and nock were correct, and server arrows already followed the
submitted view. These offsets predated admire. Previous checks measured hand attachment,
finite transforms and gameplay events, but omitted final arrow projection and camera
clearance; passing those checks did not establish a usable aiming pose.

Corrected the authored aiming placement and cant. The shared bow presentation now aligns
its actual arrow axis toward screen centre at a 10 m cosmetic reference distance, after
motion layers and reach solving. It preserves cant, refines the two-hand reach constraint,
and blends while raising. It does not alter arrow physics or server aim. Added optional
per-hand firstPersonShoulderOffset for camera-space reach adjustment without moving the
full-body shoulders. The field defaults to zero, roundtrips through saved profiles, rejects
nonfinite values and has an FPS-only workshop control. Only the bow uses the new offset.

The first aligned candidate passed direction tests but screenshots exposed hands covering
the crosshair and female near-plane slicing. That candidate was rejected. Lower/further
placement and FPS-only shoulder reach removed the clearance problem; final captures were
inspected on both bodies. The diagnostic now also requires at least 18 cm nock clearance
when raised and records hand/finger bone bounds and distances. These proxies do not replace
visual review. Initial and rejected evidence remain under .artifacts/bow-alignment and the
labelled capture folders; the accepted evidence and final results are listed below.

The native bow diagnostic exercises 220 poses across both bodies, three aspect ratios,
independent camera/item fields of view, drawing after admire and continued redraw. The
baseline reported 148 aim violations; the corrected candidate reported zero. A strengthened
engine-free loopback test independently checks authoritative arrow direction and speed
against a nonzero yaw/pitch view, alongside release replication and ammo consumption.
Final build, native and hosted validation results follow below.

Final validation: solution build succeeded with zero warnings/errors (12.13 s). All 26
focused engine-free checks passed, including the strengthened authoritative bow test.
The final native matrix passed 220 samples, both FPS-offset clone/validation checks and
zero alignment/clearance violations: maximum forward-axis angle 0.7831°, screen-centre
residual below 0.00000012 normalized, minimum raised nock depth 0.2202 m. The slight
forward-axis angle is intentional parallax from the below-centre nock to the 10 m aim
point. Both bodies' 492 combined admire samples also passed; their new bow admire contact
sheets were inspected, including idle endpoints.

The isolated hosted real-input tour passed exit 0. It checked RMB draw, two accepted LMB
shots consuming exactly one arrow each, continued-RMB redraw, idle/arrow hiding, and N
admire interrupted by RMB into another centred draw. Live centre miss at 10 m was 0.046 mm,
with both wrists attached. Screenshots bow-drawn.jpg and bow-after-admire.jpg were directly
reviewed with the ground UI present. The first launch used an occupied diagnostic port,
failed to connect, and was stopped by its exact owned PID; the retry used free port 27139
and a separate temporary save. No unrelated process was stopped and no player save or
saved graphics preference was changed. The per-run Medium preset bounded world memory.

Evidence: .artifacts/bow-alignment/{build-verified.log,tests-verified.log,verified/,
admire-male.log,admire-female.log,tour-2.log,tour-2.err}. Accepted screenshots/telemetry are
in docs/images/bow-alignment/final plus the live shots and admire sheets. The rejected
alignment-only images are retained in .artifacts/bow-alignment/rejected-alignment-only.
Native checks retain the known ObjectDB shutdown warning (one per workshop body); the
hosted run retains seven Texture and two ObjectDB cleanup warnings. No new resource-leak
fix is claimed. This is focused regression coverage, not another full 502-test run; that
suite passed before this narrow presentation change. Existing staged/unrelated work remains
untouched and this change remains uncommitted with the prior admire work.

## Video-reference source review resumed — 18 October 2026

The owner explicitly resumed this task. Read-only reconciliation found broad changes since
the prior build seal, including four shared paths from this pass. Only the rifle profile
differs from the saved first candidate: stone hatchet, salvaged axe, both accepted metal
holds and the other profiles remain byte-identical. The later rifle work is preserved.
No old seal or old build result is used as validation of current shared sources.

Review found a specific remote audio ordering error in the candidate: swish timing used
the 100 ms-delayed character render tick, while authoritative gathering impacts were
presented immediately. The new engine-free `RemoteMeleeSwingCue`, used by `PeopleView`,
selects `GameClient.ServerTickNow`; animation seeking still uses `RemoteEntity.RenderTick`.
The generic cue now expires at contact as well as after it. Three added regressions use
real content, `RemoteEntity` sampling and the production driver, with only the normally
network-fed client clock seeded through reflection. They cover clock divergence, exact
contact, late snapshots, repeated attacks and replicated same-item cancellation. Natural
hits lack attacker/attack identity, so this correction does not claim correlation under
arbitrary packet reordering. Authoritative impact handling and audio assets are unchanged.

The native locomotion diagnostic previously stopped its swing check after 67 ms. It now
also samples complete equip suppression/recovery, crouching with a stale sprint flag,
and three consecutive swings using content durations through contact, authored source
markers, recovery and natural completion. Existing screenshot tiles and tolerances remain.
Contact socket positions are recorded; they do not establish real tool-to-tree contact.
Source-bound equip's static carry pose remains an open artistic issue.

Source review and scoped whitespace checks passed. **No new build, test, Godot run, import
or capture has run.** The game was active with 3.175 GiB available at the initial check,
below the 5.5 GiB validation gate. After being informed, the owner chose “Keep the game
open for now.” The coordinator was told to hold heavy validation and leave unrelated
repair work stopped. Evidence and a current source handoff are under
`.artifacts/video-tools/resume-20261018-r1/`. Changes remain uncommitted; validation,
remaining tools' visual correction and reference-audio listening are still pending.

## Video-reference implementation started — 17 October 2026

Following the owner's progress question, three separate workers are now implementing a
bounded first pass from [the completed visual study](REFERENCE_STUDY_TOOLS_WORLD.md).
The study itself was research only. The implementation claims below are provisional until
a fresh build, focused checks and native captures have completed.

- Tool profiles: evidence-supported stone/salvaged axe candidates in
  `game/data/ground/held-presentations.json`; preserve the accepted metal-tool profiles and
  unrelated bow changes. Stone pickaxe and knife require current comparison captures before
  changing their geometry-specific placement. Other tool actions remain individually scoped.
- Moving carry: a shared, data-driven complete-rig locomotion layer for source-bound tools,
  with identity at rest and no alteration of source bone rotations or wrist attachment.
  The root owns common viewmodel/workshop hooks; the worker owns new locomotion files and
  the skeletal composition call. A bounded workshop diagnostic is being added.
- Swing cue: a shared engine-free once-per-attempt cue gate plus local/remote audio wiring.
  It delays the swish into anticipation, preserves cancellation/reconciliation and leaves
  authoritative impact audio/damage unchanged. The proposed anticipation fraction remains
  a tuning candidate; no audible reference match or subjective sound-quality repair is claimed.

No build, test, import, Godot run or capture has been launched for this pass. The existing
global recovery coordinator retains the heavy-job slot; source work here is light and
isolated by file ownership. The prepared validation sequence is
`.artifacts/video-tools/validation-plan.json`, marked not executed. These live source edits
invalidate any earlier integrated source manifest. Existing staged work remains untouched.
World/terrain lessons remain in the reference study for a separately measured environment
comparison; this pass does not duplicate the existing terrain owners' work.

Source handoff now prepared: the two axe candidates, shared locomotion layer, native
locomotion diagnostic and local/remote swish gate are written. Six engine-free swish
tests are authored, not run. A second worker's source review caught camera-pitch-dependent
speed: gameplay and the character viewer now supply gravity-planar `GroundSpeed`, with
native invariants for unchanged sprint/cadence at 0° and ±60° aim pitch and for zero gait
from vertical-only movement. The existing editor checks now select a remaining procedural
idle/swing fixture instead of assuming the stone axe must stay procedural.

The root independently verified that only the stone/salvaged axe raw profile blocks changed;
39 others, including both accepted metals and bow, are byte-identical to this pass's saved
before-file. JSON inspection, PowerShell runner parsing and scoped whitespace checks passed.
The sealed source handoff lists 1,013 source/data inputs, 12 native GLBs and 14 owned paths
at `.artifacts/video-tools/seal/source-manifest.json`. On explicit user authorization the
root contacted the existing recovery coordinator; validation is queued, with a fresh
single-job grant still required. No source-build, test or runtime acceptance is implied.

The coordinator subsequently granted only the first solution build. Fresh verification
matched all 1,013 source/data pins, 12 GLBs, the reviewed guard, HEAD and staged patch. The
single-worker incremental build passed with zero warnings/errors (MSBuild 1.91 s; guarded
elapsed 3.34 s), with at least 7,164 MiB available and at most 59% committed memory. All 15
reported DLLs retained their previous timestamps; this run did not force a rebuild. Their
actual hashes/timestamps are recorded, without claiming they were newly produced.

Afterward the source/assets still had zero drift, the staged patch was byte-identical and
both recorded owned process identities had exited. Evidence is under
`.artifacts/video-tools/validation/build-20261017-1836.{stdout.log,stderr.log,receipt.json}`
and `.artifacts/video-tools/build-20261017-1836-prep/{preflight-inputs.json,post-build.json}`.
The first-build slot is released. No focused test, Godot/import, capture, retry or commit
was included in this grant; runtime and visual validation remain pending.

## Isolated character audition — 17 October 2026

The owner requested a fresh TPS character and FPS arms with eight selected animations: idle, walk, sprint, crouch walk, jump/land, rock strike, hatchet chop and torch equip. Work is isolated in art/character_trial with outputs under .artifacts/character-trial-v1. Fresh MPFB/MakeHuman CC0 source geometry is used; existing gameplay character assets and unrelated work remain untouched. Blender 5.2.2 body generation and its first render have run. Animation authoring and validation are in progress; this is not a gameplay repair or a visual-quality acceptance. See art/character_trial/README.md for the source boundary.


Character audition review: both initial eight-clip GLBs passed structural checks, but the owner rejected the hatchet grip screenshot. Numerical wrist target agreement did not establish finger contact or anatomical wrist quality. The animation pass is paused for collaborative static-grip work. Hatchet-Grip-Study.blend isolates the rejected neutral grip for inspection. TPS elbow corrections and rock material improvements exist in source but have not been rebaked or accepted. No gameplay integration or quality acceptance is claimed.


Static hatchet review follow-up: the user identified the reversed hand/axe relationship. Read-only audit confirmed the right-hand grip used a left-handed shaft convention in animate.py and motions.py. In the live Hatchet-Grip-Study.blend only, a separate GRIP_STUDY_orientation_candidate action now preserves the user's verified untwisted hand rotation and aligns the tool shaft to the anatomical pinky-to-index direction, with the cutting edge opposite the palm normal. The candidate was visually inspected through Blender; it remains awaiting user feedback. The eight-clip generator and exported GLBs have NOT received this correction and must not be treated as accepted or rebuilt without reconciling it. A before-correction Blender copy was retained.


Static grip refinement: retained the owner's untwisted hand quaternion and corrected the axe edge to the character's forward direction (evaluated mesh yaw approximately 0 degrees). Moved the handle 12 mm toward the wrist and 3 mm toward the palm, fitted separate finger curls, and posed thumb opposition around the handle. Checked both back-of-hand and palm-side views; an initial thumb intersection was found and corrected before final save. A sampled skin-to-wood diagnostic found no sampled finger vertices inside the wood; this is not a complete collision or deformation acceptance. The live Hatchet-Grip-Study.blend is saved at frame 1 with playback range 1–1; grip-candidate-pose.json records the candidate. Renders: .artifacts/character-trial-v1/grip-forward-overview.png and grip-forward-detail.png. This is TPS static review only. FPS camera composition and animations remain a separate authoring/review pass. No GLB rebake or gameplay changes were made.

18 October 2026 — Static wrist refinement: the owner found the wrist bend unnatural. In a separate GRIP_STUDY_wrist_refinement action, a small arm-chain adjustment moves the complete hand/axe assembly about 4.3 cm inward, 1.9 cm backward and 3.7 cm upward, with a 2.9 cm elbow movement. Forearm-to-middle-knuckle direction mismatch decreases from 43.54 to 28.54 degrees. Hand/axe global orientations and all finger local poses are retained; relative-grip and blade-forward checks passed. This is a geometric pose diagnostic, not an anatomical quality certification. Hatchet-Grip-Study.blend is saved with the wrist close-up camera active; grip-wrist-refined-closeup.png records the candidate. Prior action and JSON snapshots remain recoverable. Human visual acceptance and the animation pass remain pending; gameplay assets and exported GLBs are unchanged.

18 October 2026 — Toward-viewer wrist candidate: the owner requested the hand come toward their view. Clarification was offered between wrist rotation and grip translation; work proceeded with the stated interpretation of wrist rotation. Inspection also found the live hand property at the older quaternion despite the refined action having the correct frame-1 keys. The live pose was snapshotted and frame re-evaluation restored the saved candidate; the cause of the unkeyed/stale live state was not established. A separate GRIP_STUDY_toward_viewer action turns the hand 15 degrees toward the camera about the handle axis at a fixed wrist. The prop centre follows the turn while its world orientation keeps the cutting edge forward. Elbow and wrist positions are unchanged. Finger-to-wood vertex samples found no sampled penetration; frame re-evaluation and post-look quaternion comparisons passed. Saved Hatchet-Grip-Study.blend and grip-toward-viewer-closeup.png. Visual acceptance remains pending; no animation rebake or gameplay integration.

The owner's clarification superseded that interpretation: move the whole grip closer to the camera. Reverted the additional 15-degree wrist turn, restored the evaluated pre-turn refined pose, and translated the hand/axe assembly 6 cm directly toward the unchanged camera. The two-bone arm chain follows, with approximately 4 mm elbow displacement. Hand/axe global orientations, blade direction and finger local poses are retained; rigid grip/finger checks and frame re-evaluation passed. The active saved action is GRIP_STUDY_grip_closer_to_camera, with evidence in grip-closer-camera-closeup.png and grip-closer-camera-validation.json. This supersedes the toward-viewer rotation candidate and remains a static pose for collaborative review.

## Resumed eight-motion character audition — 18 October 2026

The owner explicitly resumed the full animation test without further questions. The isolated v2 package is `.artifacts/character-trial-v2/`: a packed `Survivor-Audition.blend`, separate TPS and cropped-arm FPS GLBs, and eight independent actions per perspective. The agreed set remains idle, walk, sprint, crouch walk, jump/land, rock strike, hatchet chop and torch equip. `art/character_trial/grip_profile.json` captures the evaluated keyed static grip after the requested 6 cm translation toward the camera. The fitted right-finger rotations and hand-to-tool matrix now drive the generated animations; hatchet and torch prop bones are actual children of the right hand.

TPS locomotion and action poses use measured shoulder/elbow geometry. Review found a reversed elbow bend plane in hatchet anticipation; correcting the two wind-up keys reduced the sampled local hand angular-speed peak from about 2,851 to 1,099 degrees/s. FPS has its own choreography in `fps_motions.py`, a separate arm mesh and a `(0,-0.08,0.20)` metre viewmodel mounting offset. Initial FPS hand clipping was fixed by this placement, straighter empty-hand alignment, raised free hands and revised torch entry. Final review lenses are TPS 55 mm and FPS 22 mm, with headroom for the jump and full running shoes/torch tip in the inspected stills.

Both GLBs passed structural validation with exactly eight clips each. All sixteen Blender actions passed evaluated integer/half-frame validation: finite transforms, matching loop endpoints, rigid hatchet/torch attachment and required FPS hand framing. Remaining warnings describe half-frame quaternion-component norm drift with orthogonal applied rotations and two fast lower-arm samples during the TPS hatchet wind-up. These checks do not establish natural motion, mesh penetration, complete finger contact, planted-foot sliding or cloth collision. Actual stills from all sixteen clips were inspected; rock occludes much of its palm contact, and the skin/hands/props still need artistic quality review. This is an audition build, not a claim of Rust-level finished art or human visual acceptance.

The body, weights, skin, hair, workwear and shoes derive from the local MakeHuman/MPFB CC0 system assets; props and motion trajectories are original project work. The existing mathematical IK solver is reused. No gameplay character library, game data, ground UI, save data or original-project files were changed. Blender 5.2.2 built the assets; Python syntax and asset-specific checks passed. No C# build or Godot import is claimed for this isolated art package. Thirty-fps render output and the paired playback page are being finalized separately from these completed source/asset checks.

## Cascadeur comparison handoff and preserved Blender baseline — 18 October 2026

The owner wants to retain and further improve the Blender audition in its original chat while trying Cascadeur in a separate chat. The existing v1/v2 Blender sources and outputs were not changed by the Cascadeur export. `.artifacts/character-trial-preserved-20261018.zip` now preserves 97 files: the v2 review package (both GLBs, packed blend, all sixteen videos, stills and reports), reproduction sources, and v1 source/grip snapshots. Every archived SHA-256 was checked against the source after writing; sources remained unchanged. The archive contains its preservation receipt. Raw frame sequences remain in v2 and are not duplicated in the archive.

The v2 package has all sixteen 30-fps videos, 806 rendered frames total, validated by full decode. Browser review passed repeated paired playback, half speed and native FPS play/pause synchronization after a page event-race fix. Contact seeking remains unresolved with the current basic HTTP server: the observed browser seekable range is zero. `art/character_trial/serve_review.py` is prepared but untested and was not started. Do not report the complete player as fully validated yet.

Cascadeur 2026.2.3 is installed. New isolated files in `.artifacts/character-trial-cascadeur/` include `survivor-tps-bind.fbx`, `survivor-tps-hatchet-chop.fbx`, eight texture files, `skeleton-rest.json`, `roundtrip-validation.json`, and `Survivor-TPS.qrigcasc`. Exporter `art/character_trial/export_cascadeur.py` preserved 11 meshes, 54 bones, weights/bindings and the 1.4-second chop at 30 fps in fresh Blender reimport. The owner manually imported the bind FBX; their screenshot shows the body upright and the hatchet at its bind location near the floor. Controls have not been generated or visually validated. The quick-rig mapping generator `art/character_trial/cascadeur_setup.py` validates 51 body/finger slots but has not run inside Cascadeur. No twist joints exist; this is an investigation lead, not an established cause of the earlier wrist problem.

Native computer control was stopped by the owner because it was unreliable; continue with manual guidance or the direct API. Official installed vendor files and https://cascadeur.com/help/category/320 confirm a built-in MCP script server: Scripts > MCP > Start script server, loopback http://127.0.0.1:8765/mcp, tool run_script. It was not started, connected or executed during this handoff. Vendor README is C:/Program Files/Cascadeur/resources/scripts/python/scripts/mcp/script_server/README.md. Check modification callback return values and resulting scene state rather than trusting the outer HTTP success. Save future Cascadeur experiments as separate .casc files. No gameplay integration or final motion-quality acceptance is claimed.

## Playable terrain and cliff status audit — 18 October 2026

Read-only source/evidence audit requested by the owner after reporting weak cliff scenery.
Normal hosted play selects `WalkingWorld`; it generates spherical landmasses, blended relief
provinces, drainage, climate/material weights and natural scatter. `TerrainLod` draws adaptive
cube-sphere heightfield tiles. `SurfaceView` attaches the same walking world's `CliffField`
and monument placements in actual ground play. Cliffs are separate, overlapping near/far
scan instances, not a welded/Boolean-unioned visible surface. Their simplified triangles are
appended to the terrain collision tiles through the shared ground-collision interface.

Current placement uses approximately 48 m candidate cells and at most three columns by three
tiers, with slope, water, monument and whole-scan fitting checks. It does not guarantee
continuous coverage of an exposed cliff region. Five scan entries and six small monument
types are wired. Geological transitions, debris slopes, vegetation pockets and cohesive
scenery remain unfinished; the 16 October owner rejection still applies to visual quality.
P1's handoff trails an older pending-test state: the later coordinated foundation rerun passed
64/64, but the 18 October crosshair full-suite log now reports independent cliff, cliff-member
and monument-placement disagreements. Their cause was not diagnosed by this audit.

Earth currently selects layout variant 5 / save revision 4 (EARTH_TERRAIN.md): radius 1,200 m,
18.0956 km² total, 10.616 km² dry and 6.186 km² sampled open/gentle ground. Those suitability
measurements do not certify building placement or art. Inspected the retained P2 r4 cliff
capture and the later hosted resource-node capture; neither establishes final layout-5 cliff
quality. Later content-integration world tours did not complete loading. Native grass fixture
checks exist, but they do not establish the final combined world's visual acceptance.

Validation in this audit: inspected current source, design/status records, the retained
`.artifacts/crosshair-engine-tests.log` and two existing gameplay images. No build, test,
import, new capture, game/server launch, source/asset/save change or performance measurement
was performed. Only this audit entry was appended; concurrent work was preserved. Proposed
next work is to resolve generated-placement disagreements, then validate one continuous
ordinary-play route on revision 4 while improving cliff coverage/composition and terrain,
soil, debris and vegetation transitions. This is a recommendation, not an implemented pass.

## Grass rendering investigation — 18 October 2026

Investigated the owner's report of costly, sparse, buried-looking grass and weak color/
lighting variation. Three read-only audits covered placement, shading and performance;
root inspected the running window and original retained day/closeup captures. Findings
and code references are in [GRASS_INVESTIGATION.md](GRASS_INVESTIGATION.md). The current
field already uses vertex shaders, but submits fixed grids before shader rejection,
thins coverage repeatedly, flattens its distance fade, leaves authored crowns without
slope-contact correction, and uses nearly uniform upward lighting normals. The shaders
are lit; palette and direct-specular limitations are recorded accurately. Saved Low
settings were read, not changed. Historical performance is not reported as current.

Only investigation documentation and a local evidence bundle were added. Source hashes
are under `.artifacts/grass-investigation-20261018`. The nighttime capture was inspected
in the tool output but not preserved before the shared capture file changed; it cannot
establish daylight blade quality. No production code,
assets, player saves or settings were changed, and no build, test, import, new game run
or GPU benchmark was launched. Current full-world visual/performance validation and
repair remain open. Existing staged/unrelated edits are preserved; no commit was made.

## Grass footstep alternatives for audition — 18 October 2026

The owner rejected the current grass sound and requested downloadable or synthesized
examples to choose from. Read-only audit found the grass recipe uses BSB 2889,
"Feet in leaves #2", with four short contacts reused for walking/running/landing.
Existing measured levels do not indicate clipping; the material mismatch and limited
variation are plausible quality issues, not a listening verdict. The earlier October
16 acceptance concerned removal of the "boink", not overall grass-sound approval.

Prepared three standalone, level-matched auditions under
`.artifacts/grass-footstep-options-20261018/`: A soft grass (OwlStorm/Ashe Kirk,
variants 1/3/4), B short grass (Joseph Sardin/BigSoundBank 0854), C hiking boots on
grass (Fission9). A and C were downloaded as public HQ MP3 previews; their original
WAV masters require Freesound login. B uses the already-downloaded original WAV,
verified against the registered SHA-256. All three primary source pages state CC0.
OwlStorm variant 2 was omitted because of a reported click. No sound extracted from
another game was used. The optional game-dev CLI was unavailable; the existing
project ffmpeg/audiolib tools handled these authorized standalone auditions.

Each 8.6-second WAV has six walking contacts, a pause, then eight contacts at a
faster cadence. The faster passage re-times walking samples; it is not a recording
of actual running. D is the current game's audio at the same comparison loudness.
All four decode as 48 kHz mono PCM, measure -25.1 integrated LUFS, and remain at
or below -3.0 dBTP. No limiter/compressor or pitch variation was added. The local
manifest preserves source links, source/output SHA-256, cut/filter/gain recipes,
measurements and the pending selection status; make_auditions.py reproduces them.
ABCD-comparison.wav provides a sequential reel with starts at 0/9.6/19.2/28.8 s.

Validation is file decoding, format/duration/loudness/peak assertions and source
provenance only. No game code, catalogue, production audio, settings or saves were
changed; no C# build, engine tests or Godot run was performed. User listening choice
and subsequent gameplay integration/validation remain pending. Existing unrelated
staged and unstaged work was preserved; no commit was made.

## Free footstep packs downloaded — 18 October 2026

The owner rejected all three previous auditions as too tough and asked for complete
free sound packs. Their footwear direction is that steps should depend on equipped
boots versus bare feet; this is recorded intent, not implemented routing or approval
of any existing clip. The earlier audition manifest is now explicitly rejected.

Downloaded and extracted two complete packs outside the repository under
`D:/GameDev/Assets/Downloads/footstep-packs-20261018/`:

- MGW Sound Design, Footsteps Mini Sound Pack: 8,674,097-byte ZIP, 90 WAVs,
  including five tall-grass and five undergrowth variants. The creator's reply
  (itch.io post 11759931) explicitly allows commercial use without attribution.
- Nebula Audio, Character Footsteps (Dirt & Grass): 15,331,827-byte RAR,
  160 WAVs actually present (the page title says 80), including full/short crouch,
  walk, hard-walk and run sets plus pre/post-jump sounds. The live browser showed
  the creator's "Yes! it's royalty free." reply to the commercial-games question
  (post 9007139); the web text extractor had omitted that reply. This is creator
  permission evidence, not a claim of a CC0 license or unrestricted redistribution.

Both downloads were obtained at no cost from the authors' itch.io pages. Archive
paths were checked before extraction; all 250 WAVs decoded with finite samples.
The retained manifest records source URLs, permission evidence, archive/file
hashes and decoded format details. Four standalone listening reels are under
`.artifacts/grass-footstep-packs-20261018/`: MGW tall grass/undergrowth and Nebula
crouch grass/walk grass. Reels concatenate complete source clips with 250ms gaps,
resampled to 48kHz stereo PCM, with no EQ, gain, normalization, compression or
pitch change. This preserves original levels for audition and is not an in-game
mix or a claim of subjective softness. The lowest preview true-peak headroom is
3.8 dB; user listening acceptance remains open.

No production game audio/code/catalogue, settings or saves changed. No C# build,
engine tests or Godot launch was performed for this asset-search/audition task.
The optional game-dev CLI remains unavailable; existing audio tooling was used.
No commit was made; existing unrelated work was preserved.

## Terrain/scenery production research and implementation plan — 18 October 2026

The owner requested a researched actionable plan covering natural cliffs, soil/vegetation
transitions and the grass investigation, then asked how the same system scales across many
planet seeds. `TERRAIN_SCENERY_PLAN.md` records the proposal, with primary Facepunch, Hello
Games, Ubisoft/GDC, Guerrilla, Sucker Punch and Godot sources. Shared formation/environment
rules, compact spatial grass batches and bounded planet/tile lifetimes are the recommended
foundation. Earth is the first benchmark; a fixed multi-profile/seed matrix and global Earth
survey are required before calling the reusable system complete. Subsistence remains the
approved atmosphere reference. The local graphics adapter reports NVIDIA GeForce RTX 2070
SUPER; no runtime cost was measured.

Source triage refines the preceding audit: the three recorded independent cliff/monument
placement failures compare production layout-5 seed outputs with comparison objects built
from the unmodified base seed. This is strong evidence of wrong test inputs, not proof of
runtime nondeterminism. The plan first compares independently constructed complete worlds
using the same production profile. It also explicitly initializes offshore relief weights
and investigates the separate Jolt mesh-shape abort; neither has been executed or validated
by this research pass. No production seed or golden was changed to satisfy a test.

Implementation sequence: correct/measure, survey landforms, define complete formations,
share contact/soil/support fields, repair grass quality and submission, then verify broad
planet coverage and bounded lifetimes. Physical generation changes require compatible terrain
identities for every affected body; cosmetic/fixture fixes do not require save regeneration.
No code, assets, preferences, saves or running processes changed. Validation is source and
published-source review plus plan/document checks; no build, test, import, new screenshot or
performance benchmark was launched. Only the plan and this record were written.

## Concealment bushes investigation — 18 October 2026

The owner supplied four Rust images and requested an investigation of bushes for hiding,
especially around monuments. Added findings/proposals to TERRAIN_SCENERY_PLAN.md and recorded
the open issue in KNOWN_PROBLEMS.md. Confirmed source cause: ordinary ScatterKind.Bush is
generated and has procedural meshes, but normal ground play uses NaturePlants over the
resource-only INaturalObjects provider, which excludes ordinary bushes. Monument circles
also suppress generated vegetation. No foundation-to-foliage suppression path was found.
Existing shrub LODs shrink/cull at quality-dependent distances (90 m Low, 156 m Maximum),
so merely reconnecting the mesh would not establish fair concealment.

Primary Facepunch publications document tall bushes intended for hiding, foliage shaders/
wind/billboards, bush displacement when building, population replenishment, clustered
placement and replicated movement-sensitive rustles. These establish useful reference
behavior; they do not reveal every current proprietary implementation detail. The owner's
unchoppable observation was not independently playtested. Proposed work separates scenery
from berry harvesting, preserves IDs when composing plant sources, adds monument planting
pockets and shared foundation clearance, and preserves concealment across quality levels.
Detailed gameplay, regrowth, asset selection, distances and performance budgets remain open.

Validation: inspected current C# source/data, existing local asset-audit documentation and
published primary sources. No game code, assets, settings, saves or processes changed;
no build, test, import, new screenshot, game run or performance benchmark was performed.
The three documentation edits preserve unrelated staged/unstaged work; no commit was made.

## Inventory backpack DROP ALL — 18 October 2026

The owner requested DROP ALL and explicitly confirmed backpack contents only. The button sits beside
INVENTORY, is disabled for an empty backpack, and keeps the six hotbar/belt slots, worn equipment and
open loot untouched. Its tooltip explains that scope. The existing scaled UI and ordinary lootbag
placement/merging are reused.

One bounded DropAllIntent carries all 24 displayed slots. InventoryRules.RemoveAll checks every slot,
including empty slots, counts, condition, loaded ammo and fitted attachments, before removing anything.
Client prediction uses the same rule. The server spends one intent-budget unit and spills authoritative
stacks through SpillSystem; a stale snapshot rejects the whole operation and prediction rolls back.
The generated protocol schema hash changed; client and server must use matching builds.

Validation was isolated from the checkout's other ongoing changes at base 475ffb14. The solution built
with zero warnings/errors. DropAllTests passed 3/3, covering a full 24-slot backpack, exact item conservation,
nearby bag merging, weapon metadata, preserved hotbar/wear/open loot, wounded refusal, stale/new items,
prediction rollback, double-click suppression, duplicate requests, malformed snapshots, overflowing counts
and forged counts. Native Godot --ui-check passed 57/57, including actual button input. The headless UI
attempt did not paint mouse hitboxes and is not accepted as UI validation. Screenshots at
[1600 × 900](images/drop-all/inventory-1600.png) and [1280 × 720](images/drop-all/inventory-1280.png)
were visually inspected.

The full engine-free suite passed 409/410 in 468.9 s. HeldActionTimingTests.MeleeHitMarkerCarriesTheAuthoritativeToolTickAndContactPoint
failed in that full run; the complete HeldActionTimingTests group then passed 8/8 on this change, and
the single failing test passed 1/1 on an unchanged base snapshot. This records an intermittent timing
failure, not a claim that the full suite was entirely green or that its cause was fixed. The first
non-isolated focused attempt could not load concurrently edited electricity data with the older compiled
content readers; it did not validate this feature. Logs are preserved under .artifacts/drop-all/isolated/.

Cost: one request and bounded 24-slot snapshot validation per click, plus ordinary bag packing and
replication; the button checks for backpack contents while inventory is painted. No new background
simulation system is introduced. Existing loose-bag lookup cost grows with loose world objects and is
repeated for each nonempty stack; crowded-world performance has not been benchmarked. These regression
checks found no duplication in their covered cases and are not a proof against every possible exploit.

## Current item and component art audit — 18 October 2026

Read-only asset/catalogue review requested by the owner: 137 current items, including
14 components and 17 resources. All item keys have model mappings; all 171 catalogue
base-model/stage paths exist. Six components already select the revised Hollow_Salvage_R01
textured scenes and supplied 128/256 PNGs: gears, springs, metal pipe, rope, tech scrap
and sheet metal. Fresh SHA-256 checks match all 34 installed salvage-package files and
15 container-package files against assets/revised/integration.json. No unused additional
component design was found in that source package.

Eight components still have explicit borrowed-model stand-ins and no rendered icon metadata:
cloth/sewing kit use fibre; metal blade/sign plate/gun receiver/rifle frame use refined metal;
torn/sealed schematics use the building plan. CatalogItemIcons consequently delegates these
to the flat SVG placeholders in ItemIcons.cs. Salvage, blueprint and research bench also
use that fallback. Explosives deliberately reuse the sulphur model and PNG. Across all items,
123 have dedicated PNG-backed entries, two alias existing PNGs and twelve fall back. The
twelfth fallback is Wire Tool: its configured 128/256 PNGs are missing, and no specific SVG
case exists, so it receives the generic placeholder. Revised does not mean final art approval;
the major component inconsistency is incomplete replacement, not low polygon count alone.

Diagnostic current component/resource sheets, two whole-catalogue icon sheets, a machine-readable
inventory and reproduction script are under .artifacts/item-art-audit-20261018/. These compose
the currently selected PNG files and source SVG drawings (rasterized outside Godot); they are
not fresh gameplay screenshots. All four sheets were visually reviewed. Existing native Godot
captures in docs/images/abtest-b/repairs/assets/ were reviewed for the revised models. No game
code, production assets, settings or saves changed, and no build, test, import or Godot session
was run. Only diagnostic outputs and this audit record were written; other ongoing work was
preserved. Asset replacement and in-game visual acceptance remain future work.

The owner subsequently requested a saved plan and commit. `ITEM_ART_REPLACEMENT_PLAN.md`
records thirteen scoped item entries, the three initial quality examples (cloth, gun
receiver and salvage), model-to-icon production, blueprint/lootbag behavior to preserve,
Research Bench placement/destruction checks and implementation acceptance requirements.
The audit sheets and portable catalogue snapshot are preserved under
`docs/images/item-art-audit-20261018/`; `tools/audit_item_art.py` generates fresh diagnostics
with explicit runtime/font/output arguments. The generator was run successfully. Concurrent
work has now supplied both Wire Tool PNGs, so the refreshed count is 124 dedicated PNGs,
two aliases and eleven fallbacks; the saved sheets retain the initial audit baseline.
The plan marks Wire Tool for model/icon review. Document links, snapshot counts and commit scope were checked.
This is documentation and diagnostic tooling only; no C# build/tests or Godot run was
needed for this commit, and the asset implementation remains planned.


## FPS wrist and strike-path refinement v3 — 18 October 2026

The owner kept Cascadeur experimentation in a separate chat and requested further Blender FPS
work: the torch and hatchet right wrist bent sideways/downward, and both strikes needed larger
wind-ups and forward reach. The v2 package and verified preserved archive remain intact. The
new review candidate is `.artifacts/character-trial-v3`, served at `http://127.0.0.1:8873/index.html`;
the comparison link opens unchanged v2 on port 8874. No gameplay assets were replaced.

The reused TPS palm orientation caused the large FPS wrist bend. FPS torch/hatchet hands now
follow the solved forearm with small anatomical offsets; the FPS hatchet rotates around its
shaft independently so the cutting edge faces forward. Both rock hands solve around one shared
contact frame. The three motions have revised arm/elbow paths; the rock and hatchet wind up
higher and extend farther. Finger curl, meshes, skin weights, camera and TPS motion are unchanged.
`refine_fps.py` opens the immutable v2 blend, writes a separate blend/GLB and checks a TPS action
digest. TPS GLB bytes remain identical to v2.

`fps-refinement.json` records maximum hand/forearm angles before/after: rock 44.84/0 degrees,
hatchet 94.75/7 degrees and torch 71.93/5.98 degrees. Prop-origin vertical travel increased
21.8 to 34.7 cm for rock and 13.8 to 35.3 cm for hatchet; forward-axis travel increased
10.9 to 16.9 cm and 10.7 to 17.2 cm respectively. These are trajectory ranges, not a claim of
that much additional impact reach or an assessment of finger contact. The early hatchet elbow
pole was adjusted after a subframe audit found a rapid pre-impact change; peak bend-plane
speed fell from 1479 to 847 degrees/second, with the highest speed now on the downstroke.

Validation: both GLBs pass structural checks; all 16 saved actions pass the integer/half-frame
animation checks, including rigid attachments, loops and required FPS wrist/palm framing.
Raw interpolated quaternion norms still produce warnings where the evaluated rotations are
orthonormal. Twenty diagnostic FPS/side renders were inspected. The revised three clips were
rendered at 30 fps (135 frames total), encoded and fully decoded. The other 13 videos retain
verified baseline media/receipts; the complete 16-video report passes. Hatchet and torch wrist
continuity and the longer rock arc were checked visually. Tool tips can briefly leave the top
of the frame during wind-up; unchanged left-hand posing still needs artistic refinement.

The review server now supports HTTP byte ranges and conditional range requests; 22 server
checks passed. Videos and stills have content-hashed URLs after browser verification exposed
cached v2 video under reused paths. Browser checks confirmed the actual v3 hatchet MP4 at
contact, working seek and synchronized quarter-speed looping without decoder errors.
Python/generated JavaScript syntax and source whitespace checks passed. No C# build, Godot
import or gameplay test was run for this isolated Blender revision; visual approval and runtime
integration remain separate steps.


## Grass-footstep reference and Nox audition — 18 October 2026

The owner rejected the MGW/Nebula previews for ordinary footsteps: they resemble bush
entry/exit/traversal, not regular ground contact. This is not acceptance for bush audio.
The working brief is soft foot-ground contact on short grass or firm soil with little
vegetation rustle. The supplied listening reference is https://youtu.be/el312OFsaMQ;
it is recorded as a reference URL only. No audio from Rust or the video was imported.
The assistant cannot receive audio in this tool setup, so no listening comparison,
subjective quality claim, footwear identification or reference match is asserted.

Downloaded Nox Sound Design's complete Essentials Series archive from the creator's
https://nox-sound-design.itch.io/essentials-series-sfx-nox-sound page, which states CC0
and permits personal/commercial use. Source archive and page evidence are outside the
repository under D:/GameDev/Assets/Downloads/footstep-packs-20261018/nox/. The archive
is 1,036,374,981 bytes, SHA-256
0df9a98cc8d0499787cb3c29f3e278fdacd75ca3e94106828ea8b5c115ae8d3d.
Only its two footstep collections were extracted: 479 Essentials and 210 Iceland WAVs.
All 689 decoded to finite samples and have individual hashes recorded in manifest.json.
The advertised short-grass files are named Snow_Short_Grass; they were not selected.

Two 4.75-second audition WAVs and their reproducible script are in
.artifacts/grass-footstep-contact-20261018/: Nox-DirtyGround-walk.wav and
Nox-Grass-walk.wav. Each uses the first six numerically ordered full source clips,
750 ms between starts and a uniform +6 dB audition gain. No EQ, compression, trimming
or pitch changes. Output is 48 kHz mono 24-bit PCM; finite-sample/readback checks passed,
and ffmpeg measured true peaks of -9.1/-6.7 dBTP respectively without clipping.
Source footwear is unconfirmed. User listening acceptance remains pending.

The prior pack manifests now record the rejection and reference. No game audio files,
code, catalogue, settings or saves changed. No build, gameplay test or Godot run was
performed for this download/audition-only work. Unrelated concurrent work was preserved.


## Nox grass/dirt footstep softening audition — 18 October 2026

The owner finds both Nox auditions better and requested a small reduction in bass and
softer impacts. Prepared revisions under .artifacts/grass-footstep-contact-20261018/softened/
with a reproducible make_softened.py and provenance/measurement manifest. Existing source
WAVs and their first auditions were preserved. Both revisions apply a 45 Hz high-pass,
-3 dB bass shelf at 200 Hz, -1 dB treble shelf at 4 kHz, gentle 1.4:1 peak compression
(0.7 ms attack, 40 ms release, -22 dBFS threshold, no makeup), and -0.5 dB output gain.
No pitch, cadence or duration changes; no normalization afterward. The earlier +6 dB
audition gain is already in the input and is not applied again. Comparison WAVs contain
the full original then the full revision with 0.5 seconds of silence between them.

Both 4.75-second outputs decoded successfully as finite 48 kHz mono 24-bit PCM with
228,000 frames. Bass/mid spectral-energy ratios (30-200 Hz versus 500-2000 Hz) decreased
3.75 dB for dirt and 3.89 dB for grass. ffmpeg measured true peaks -12.7 and -9.6 dBTP,
respectively, down from -9.1 and -6.7 dBTP; no clipping. The assistant cannot listen in
this setup, so these are measured processing results, not subjective approval. User
acceptance of the revised timbre remains pending. No game code, production audio,
catalogue, settings or saves changed; no build, Godot run or gameplay test was needed
or performed for these audition-only edits. Unrelated concurrent changes were preserved.

## Environment architecture and T0 correctness/lifetime foundation — 18 October 2026

The owner authorized a coherent, scalable terrain/scenery architecture and an additional
worker. WORLD_ENVIRONMENT_ARCHITECTURE.md now defines the implementation contract and
migration gates; TERRAIN_SCENERY_PLAN.md retains the researched T0–T6 delivery sequence.
The contract covers immutable planet descriptors, named deterministic seed domains,
separate physical/population/appearance revisions, bounded world leases and work budgets,
formation/support/substrate stages, vegetation ownership, concealment compatibility and
spatial rendering. It explicitly distinguishes shipped groundwork from unimplemented work.
The external worker's assignment and subagent file ownership were separated; root alone
ran builds and Godot for this package. The previous recovery queue was not restarted.

Implemented this checkpoint:

- Corrected three independent cliff/site comparison fixtures to construct the same
  production profile and layout seed. Explicitly clear offshore relief weights in
  WalkingWorld.Compute. Added complete-sample/order/thread checks using independent
  layout-0 and layout-5 worlds, including 204 directions, 256 formation tiles and four
  workers. These distinguish a bad fixture from an actual generation defect.
- The stricter sample contract exposed BiomeTable.Weights summing to 1 + ice*alpine.
  Alpine already occupied an absolute share; ordinary land now receives the remaining
  1 - ice - alpine share. Added independent biome-partition checks. Terrain height
  goldens remain 0x3FD0EDEC0D0C72A5 for Earth and 0x1D83ABE87CA2D9EA for the inherited
  walking fixture. Layout variant remains 5. No landform seed or height golden was changed.
- Biome-dependent populations can change, so Earth terrain/save revision is now 5,
  other walking bodies 2, and protocol 37 prevents mixed builds. Previous saves are
  preserved and explicitly refused; fresh defaults use the new revision. Added read-only
  refusal tests, including byte-for-byte preservation. No existing user save was opened
  or migrated by this work. GeneratorVersion.Surface was not changed: it would reroll
  the real-radius terrain seed for an unrelated reason.
- PlanetBodies resolves metadata without constructing a real-radius PlanetSurface.
  Converted metadata-only gameplay/presentation callers and WalkingWorlds lookup.
  Invalid addresses are not retained in terrain caches, and invalid walking radii are
  rejected. WalkingWorld's thread-static last-sample identity no longer holds a world
  back-reference. Successful world caches are STILL static and unbounded; this is not
  the planned WorldStore/lease implementation or multi-planet lifetime acceptance.
- TerrainLod now owns an isolated worker session for each Setup. Stop/reset discard
  old results without blocking the scene thread; workers finish against the captured
  old surface and cannot publish into its replacement. Required failures remain visible;
  a late failure from an already-discarded tile cannot poison current readiness. Explicit
  mesh/staging-array disposal and managed payload transfer reduce avoidable retention.
- Grass workers use an immutable managed context, observe eventual faults after scene
  removal, and have idempotent retirement before replacement/QueueFree. Added separate
  grass/litter/authored layer masks and submission counters for later controlled runs.
  Removed an unused retained face-lattice reference. Placement math, density/LOD policy,
  palette, shader lighting and whole-face batches are not redesigned in this checkpoint.
- PhysicsWorld.Dispose now drains every pending tile worker even if one fails, releases
  native resources, then reports aggregate failures. Controlled production-path tests
  verify a blocked build does not hold the Jolt gate, remaining work drains, resident
  props/tiles and the native world release, and a second Dispose is safe. This closes
  the observed disposal exception path, not the previously recorded native access violation.

Validation and limits:

- Latest Debug solution build: zero warnings/errors, recorded in
  .artifacts/environment-architecture/build-release-input-debug.log (despite its name,
  this is a Debug build). The integrated 42/42 focused tests passed in 6.8 seconds;
  focused-integrated-debug.log includes determinism, biome partition, compatibility,
  metadata resolution, architecture and physics lifetime checks.
- After available memory recovered, the final Release solution build also completed
  with zero warnings/errors; build-integrated-release.log. The same 42/42 focused tests
  passed in Release in 6.4 seconds; focused-integrated-release.log.
- Actual Godot 4.7.2 .NET native headless lifetime probe passed 28/28 checks after the
  final retention cleanup; native-lifetime-retention.json/log. It exercises terrain
  replacement, retirement, required/stale failures, grass publication/retirement and
  layer masks. Seven deliberately injected build failures produce expected error logs.
  Dummy/headless rendering is not GPU visual or performance validation.
- A single-process full Debug suite was stopped at approximately 5.07 GB test working
  set because available system memory was low. It had reported the large-furnace missing
  fire-effect catalogue failure. It did not complete; no full-suite pass is claimed.
- Added exact-class discovery/execution to the test runner and a serial process-isolated
  PowerShell runner with timeout/memory guards. Its first raw-summary regex incorrectly
  rejected CRLF endings. Fixed to accept the carriage return and reparsed retained stdout
  without rerunning successful tests. Corrected results.json and summary-corrected.txt
  retain the actual outcome: 88/89 tests in 15 completed classes, then BatteryTests was
  stopped by the memory guard during ARocketStrikesAHutAndAnIncendiaryLeavesAFire.
  AttackStationTests.SpamCannotProduceMoreThanTwoShotsAcrossLatencyJitterAndLoss failed
  (expected two authoritative shots, observed zero). Only 16/122 classes were attempted.
  The original invalid aggregate is retained for audit. These broad failures are not
  assigned to the terrain change without investigation. Isolating tests does not prove
  that a long-running game releases its worlds correctly.
- Hosted Vulkan/Forward+ High at 1600x900, fixed cliff site/time and a fresh diagnostic
  save joined the ground scene and uploaded grass but produced no screenshot within
  the 180-second outer bound. The capture log occurs after FramePostDraw, so this does
  not prove terrain readiness was the blocker. A traced High retry hit the memory guard
  at 19 seconds. A Low/1280x720 retry hit it at 1.4 seconds before world loading. Logs,
  fresh saves and result receipts are under .artifacts/environment-architecture/.
  Each termination targeted only the process created by this task. A separately running
  full-test process was observed later; it was not stopped or claimed as our run. Available
  system memory fell below 1 GB, so further GPU runs were deferred. Release validation
  was performed only after memory recovered, as recorded above.
  No successful new gameplay screenshot, controlled GPU timing or scenery approval exists.
- The final isolated-runner script passes PowerShell syntax parsing. Its preflight memory
  guard and CRLF parser are present; a full successful end-to-end runner run remains open.

Saved graphics preferences were not edited. No reference-game assets were imported.
Unrelated staged/unstaged work was preserved and no commit was made. WorldStore/descriptor
negotiation, full formations/support/soil, spatial grass batches, grass contact/lighting,
ordinary concealment bushes and multi-seed visual/performance acceptance remain pending.
The next bounded package is explicit world ownership and descriptor identity, followed by
formation/support/substrate and ecology/rendering work under the contract's removal gates.


## Approved Nox footstep integration and footwear audit — 18 October 2026

The owner approved both softened auditions as better, usable but not perfect, and asked
whether these were only two clips and how surface/footwear affected playback. Each reel
contained six distinct source contacts. The downloaded Nox library contains 689 footstep
WAVs (479 Essentials plus 210 Iceland); Essentials includes 94 grass and 26 dirty-ground
recordings across movement types. Only the twelve auditioned contacts are admitted here.

Installed six softened grass and six dirt OGGs through the existing audio pipeline,
replacing the older 30 grass/dirt gait duplicates with shared banks. All four gait/contact
events per material reference the same six contacts. The complete footstep catalogue now
has 32 events across eight surfaces and 126 unique footstep files (plus four swim files).
Runtime still chooses surface and movement, shuffles variants per event and applies modest
pitch/level jitter, distance attenuation, gait gain, local -3 dB and landing-speed gain.
The Nox banks preserve the approved EQ/compressor order and +6 dB pre-compressor gain,
without normalizing their quiet levels back upward. The new footstep-soft category has
an event target of -35 LUFS and validation bounds -40 to -31; normalize=False preserves
natural differences. Full contacts replace the earlier 0.32-second cap. A 40 ms maximum
ending fade addresses the encoded tail check; the final worst tail is -53.3 dB relative
to peak. No generic bass thump is added. Existing normalizing recipes keep their prior
hashes and behavior. No other event content or item-audio map changed.

art/audio/nox_footsteps.json records creator/CC0 provenance, the 1,036,374,981-byte archive
SHA-256 and a closed roster of twelve source member hashes. sources.pack extracts only
those members and verifies cached bytes. The manual-download source reports its creator
page/exact destination when absent; --fetch now exits unsuccessfully if any download
remains missing. The game-dev CLI required by the optional vendoring skill is unavailable;
this work used the repository's established hash-checked pipeline, not a claimed game-dev
package admission. Source archives remain external, source cache is ignored, and encoded
output hashes/measurements are in game/assets/audio/levels.json. Credits and GROUND.md /
REPAIR_AUDIO.md describe the replacement. No other game's audio was imported.

Footwear currently has no effect: FootInput and local/remote call sites do not read worn
items for audio, even though equipped feet-layer items are available. Surface routing
already handles terrain, built-floor material, ships and shallow water. A future footwear
system should choose appropriate contact recordings or layers with shared surface sound
and runtime variation; this is a recommendation, not implemented support or a claim that
these Nox recordings are barefoot. No simulation or network behavior changed.

Validation: solution build passed with zero warnings/errors; all 264 source downloads
verified, none missing. Final Godot import completed; a native crash on the first reimport
after the tail adjustment was resolved by retrying without changes (both logs retained).
The final engine sound-check loaded/measured 235 events and 793 file references with zero
problems, maximum meter disagreement 0.9 LU and highest true peak -1.14 dBTP. All five
AudioRepairTests and both file/import and level/hash catalogue checks pass. The remaining
SoundCatalogueCoversItems check fails on existing unmapped building/deployable items;
the identical failure is reproduced with the original HEAD sounds.json and current item
data in an isolated fixture. Item mappings are unchanged; only eight grass/dirt events
and their new category differ. Thus the targeted group is 7/8, not wholly green. Broad
all-game test runs were interrupted and are not claimed complete or passing. A final
incremental audio build changed zero files/imports and checked 130 footstep/swim files.
Logs, baseline proof and receipt are in .artifacts/nox-footstep-integration-20261018/.
No interactive gameplay listening pass was performed; source listening acceptance is the
owner's, and in-game mix approval remains open. Other concurrent work was preserved.

The external worker completed the read-only follow-on ownership audit at
.artifacts/environment-architecture/worker-t0/worldstore/WORLDSTORE_LEASE_HANDOFF.txt,
with 72 source hashes. It maps server landing/bootstrap, zone/navigation, client prediction,
shared bots, rendering, maps and tools into seven migration slices. Integrated eviction is
explicitly gated on all asynchronous readers retiring safely; multiplayer remains restricted
to descriptors reconstructible from the existing wire/shared build until negotiation exists.
This audit adds no production implementation or validation claim.

## 18 October 2026 — footwear-aware recorded footstep banks

Implemented the owner's request for barefoot/shoes/boots and more variations per surface.
The follow-up explicitly reserves shoes for future clothing rather than adding an item now;
at least five or six future shoe items can share the shoes profile or declare additional
registered profiles. This section supersedes the earlier entry's statement that footwear
has no audio effect.

WearStats now exposes wear.footstepProfile, validated as a lowercase identifier and allowed
only on the Feet wear layer. work-boots declares boots; unequipped feet resolve barefoot.
FootwearAudioData reads game/data/ground/footsteps.json and resolves the predicted local
wear inventory and remote replicated worn item IDs through the same capability. Cadence
keeps its surface/gait key; FootstepDriver resolves the corresponding profile event only
when a contact is emitted. Swimming stays shared. Unknown optional profiles/banks have
safe default/legacy fallbacks, while authoring tests report missing assignments/banks.
There is no per-item switch, network message change, or new shoes item. Existing preload
covers all suffixed events; per-frame profile lookup allocates no worn-item array.

The version-2 Nox roster admits 193 unique clean source recordings for grass, dirt, sand,
rock, snow, wood, metal and shallow water. Walk/run/land counts are respectively
16/10/6, 10/10/3, 12/10/3, 9/10/1, 12/10/3, 10/10/2, 12/10/3 and 10/5/6.
Crouch shares walk recordings. Vegetation rustles, jump takeoffs and clipped rock landings
are excluded. The 193 sources produce 579 profile OGGs (4,981,445 bytes), plus four unchanged
swim outputs. The earlier six grass and six dirt sources remain in the walk banks.
These are derived footwear treatments: the source pack does not identify the performer's
footwear, so no claim of separately recorded authentic barefoot/shoes/boots is made.

Shared processing retains the accepted light bass reduction/compression; barefoot receives
additional high-frequency softening and -2 dB event gain, shoes the base treatment, boots
a modest mid/high adjustment and +1 dB. Gait gain/range and profiles are data-driven in
footsteps.json. Outliers are calibrated into -38.5..-32 event LUFS, with a -8 dBTP ceiling;
contacts already within that interval preserve their natural level differences. All 579
outputs meet the measured category and fade checks: highest true peak -8.5 dBTP, worst
ending tail -48.2 dB. Filters are baked, not added as per-character runtime DSP.

The repair/full builders now honor generated volume/range changes and prune removed
footstep events, while retaining unrelated sound events and hand-tuned playback fields.
Two authoring regression tests cover those rebuild behaviors. Profile names are checked
before output paths are constructed. Expanded extraction verifies each selected member
and supports a previously populated smaller cache; archive/member hashes and licence
provenance remain pinned. The established hash-checked pipeline was used because the
optional game-dev CLI is unavailable. No other game's audio was imported.

Validation: all 264 source downloads verified (zero missing); Godot imported all 579 new
OGGs successfully. The engine sound check loaded/measured 331 events / 1,767 file references
with zero problems, largest meter disagreement 0.9 LU and highest catalogue true peak
-1.14 dBTP. Two Python authoring regression tests pass. A final incremental repair changed
zero audio files or import options and checked all 583 footstep/swim outputs.

The initial solution build successfully compiled Hollow.Simulation and Hollow.Client,
including the new footwear code, but failed in concurrently edited environment/vegetation
files. Subsequent solution builds remained blocked by EnvironmentDescriptors.cs using
Galaxy.DefaultSeed (static readonly) as an optional parameter constant. A client-only build
against those successful dependencies also encountered unrelated map changes: the current
Map.Render cancellation overload was not in the existing dependency, and GroundUi.Globe.cs
uses Godot.Collections.Array<Image> in a using declaration although it is not IDisposable.
These files were not modified by this audio task; a full fresh build is NOT claimed passing.

To validate the new tests without changing those concurrent files, an isolated test project
links the repository's actual test sources against the just-built simulation/client and
existing dependencies. All five new FootwearAudioTests pass, all five AudioRepairTests pass,
and the catalogue file/import and level/hash checks pass (12/13 in the targeted group).
The sole failure is the already demonstrated SoundCatalogueCoversItems gap for unrelated
building/deployable item mappings; the item-audio map remains unchanged. All three content
tests also pass. The existing engine binary verifies audio assets, not an interactive run
of the new footwear routing. No full-suite pass or in-game listening approval is claimed.
Logs and the isolated check project are in .artifacts/footwear-audio-20261018/. The owner's
listening approval covers the initial twelve softened contacts; the expanded bank awaits
in-game listening feedback. Other concurrent work and staging were preserved.

## 18 October 2026 — bounded world ownership integration, continued

The world store now feeds the server's landing/bootstrap, zone, client prediction, hosted
view, maps and shared bot contexts through explicit leases. ClientWorldSession,
ZoneEnvironmentOwner and ServerSessionResources own construction rollback and ordered
retirement; gameplay hosts retain message/tick dispatch. Read admission closes before
retirement, and source pins survive admitted asynchronous readers. The old static full-world
cache entry points have been removed. Store byte budgets are conservative admission
estimates; they do not bound all native renderer memory or claim a measured frame budget.

Repeated Welcome retires the old replica before publishing its replacement, including a
same-descriptor reconnect. GroundView now resets buildings, things, ships, people,
placement, fire/raid effects and developer tiles through one production retirement method.
The native reconnect fixture calls that same method rather than duplicating its wiring.
Maps replace their source delegate even when their disk-cache identity is unchanged.
Inventory preview ownership is stamped so an old view cannot clear its replacement, and
the view detaches its intent-answer handler. Shutdown attempts the remaining owners after
a cleanup failure and reports aggregated errors. Snapshot baselines reset on replacement.

Earlier integrated evidence in .artifacts/environment-ownership: Debug builds r2/r3 passed
with zero warnings/errors; ten isolated classes passed 57/57 checks; native lifetime r1
passed 45 checks with exit zero. That native run exercised terrain, grass, vegetation and
SurfaceView retirement, including expected injected faults. These runs PRECEDE the latest
host-helper/reconnect/cache/spatial-grass edits; current source still requires a fresh build
and rerun. Full-suite failures and the earlier incomplete native results remain unresolved.

The current cliff cache has a 384-entry LRU limit including empty/pending entries, resident
request coalescing and identity-checked fault removal. Its physical generator was preserved.
In-flight eviction may permit duplicate caller-owned work; this is a retention bound, not
a universal execution-concurrency cap. Natural-object caching is undergoing the same audit.

The external worker is adding inactive, engine-free formation topology, immutable physical
records, triangle support and substrate contracts with synthetic tests. A separate grass
owner is implementing an explicit --spatial-grass comparison path; defaults retain the
whole-face baseline pending inspection. No formation activation, final soil/habitat
integration, spatial-grass promotion, visual acceptance or performance improvement is
claimed by this checkpoint.

The bounded capture runner now distinguishes idle reusable compiler servers from active
builds, validates decoded PNG dimensions and records readiness, readback, window state,
memory and task-owned cleanup. Preflight r1 did not launch because capture diagnostics
needed rebuilding; it also recorded only 1,313 MiB available RAM. The coordinator requested
memory headroom and preserved the 4,096 MiB start / 1,536 MiB running guard for the proposed
High capture. No unrelated application was closed. No current hosted baseline PNG exists.

Follow-up validation on the frozen integration: Debug build r4 found a wrong namespace on
the newly named intent-answer handler; corrected to Hollow.Client.IntentAnswer. Debug r5
and Release r1 then passed with zero warnings/errors. Focused Debug r2 and Release r1 each
passed 103/103 checks across 16/16 classes, including 28 new formation-core tests, five
natural-cache tests, five cliff-cache tests, the expanded client reconnect cases and host
architecture limits. Native lifetime r2 passed 70/70 checks in 2.46 seconds, exit zero, with
exactly ten expected injected failure logs and no guard termination. It uses a headless
renderer and tiny synthetic resources; it does not validate the production grass shader,
rendered terrain contact, GPU culling or frame cost. Release broad-suite continuation is
running separately; no complete-suite pass is claimed yet.

The static cliff audit (.artifacts/environment-ownership/cliff-asset-audit.md and its metrics)
verified 50 active visual/dependency hashes plus shared collision provenance. Upward
collision triangle-centroid distance to near visual geometry has unscaled p95 values of
8.0/10.6/22.2/26.4/8.1 cm across the five scans; these are samples, not complete mesh error
bounds. Existing shelves need explicit qualified boundaries and accurate shared support
geometry before soil or plants can rely on them. No scan export or live cliff change was
made by this audit. The unavailable optional game-dev CLI was not represented as executed.

## 18 October 2026 — memory-constrained environment validation, continued

The owner directed that visual verification remain required and that work proceed within available
memory. No unrelated application was closed. Broad Release continuation r1 completed 14 classes
(77/84 tests) before the fifteenth, BatteryTests, was stopped by the memory guard. Seven projectile
fixture failures used reflection to set properties now owned by ClientWorldSession; the fixture now
opens through the actual Welcome lifecycle and drains retirement. Those corrections await rerun.
The interrupted Battery class is incomplete, not a pass or an intentional exclusion.

Hosted High diagnostic r2 used the ordinary child server and unchanged High scenery settings at
1600x900, with a 2,560 MiB startup floor and 768 MiB running floor. These are operational safeguards,
not product memory budgets. At 16.86 seconds, after joining and loading terrain arrays and the first
character library, free RAM reached 682 MiB. The verified owned processes totalled 2,415.88 MiB of
working set. The guard stopped them and verified cleanup. No PNG was written; the run did not reach
visual or performance acceptance. Its logs do not establish that tree-impostor processing had begun.

Source follow-ups preserve quality while reducing avoidable allocations: tree-atlas finishing uses
float bands with dilation halos and streamed mip packing, with byte/float parity fixtures against the
previous algorithm; catalogue warm-up bounds temporary preview owners and retires them only after
draw frames, including declared delays for effects. Camera retirement also detaches these previews.
The catalogue's shared loaded resources remain separate from this temporary-node bound. Native
preview, cancellation and atlas checks are added but not yet run on this source. Capture logging now
includes managed heap/commit and startup phases to distinguish managed from total process memory.

Stand-in natural-object retention now follows the same 384-entry pending/empty LRU contract as the
walking provider; generation is unchanged. Review found that each 2 km station ray searched a
roughly 1 km-radius disk, causing excessive work and now potential cache churn. A conservative
segment broadphase is under implementation; no damage-equivalence pass is claimed yet. Tests now
use workstation concurrent GC, matching the dedicated server, and optional HOLLOW_TEST_MEMORY=1
reports process/managed memory at test boundaries without forcing collections. This is not evidence
that the earlier multi-GB growth has been fully attributed or fixed.

Detailed near-mesh cliff preparation remains review-only. Crown candidates have pinches and holes;
their exact boundaries must survive qualification. No soil/habitat permissions, asset replacement,
live formation activation, spatial-grass promotion or finished-world claim is made here.

Further coordinated validation: full Debug solution r6 passed with zero warnings/errors. The final
preview retirement and atlas processing fixes passed a game-only Debug r7 build against r6 libraries,
then native lifetime r3 passed 253/253 checks, including float/byte atlas parity and actual deferred
preview destruction. Exactly ten injected fault logs were expected. This is headless lifecycle/data
evidence, not production GPU or scenery approval. The atlas processing change reduces estimated
temporary managed arrays while preserving output; it has not established a measured game-memory gain.

Hosted High r3 started with 5,183 MiB free and was stopped at about 43 seconds when available memory
reached 696 MiB. Peak task-owned working set was 4,622.43 MiB; the client accounted for 4,437.01 MiB.
At the last readiness sample the client was network-Playing but the loading screen/world remained
unready. Managed heap was about 147 MiB. Atlas baking had begun, but its pixel-processing/readback
phase had not; no PNG was written and cleanup was confirmed. These facts do not identify a leak or
attribute the preceding growth to the atlas. New opt-in phase telemetry separates renderer texture,
buffer and total accounting, engine static memory, process working set/private bytes and managed
heap. Renderer accounting is not measured physical VRAM residency. Warmup pins its loaded catalogue
resources independently of bounded preview nodes; no ground source textures are among those pins.

The shared natural-object segment query now serves both authoritative combat and client NaturalAim,
with conservative shape reach and unchanged narrow-phase selection. The projectile fixture uses the
actual Welcome-owned world and session attack identity. Full Debug r9 passed (zero warnings/errors,
56.41 seconds) after r8 found a sign-extension warning in the new planner's packed identity; explicit
unsigned casts fixed it. Focused Debug r5 completed 68/70 tests: decoder 8/8, package loader 10/10,
planner 7/7, NaturalAim 3/3, projectile 14/14, battery 10/10 and architecture 5/5. Chart tests passed
11/13; two assertions are under investigation. Battery's lagged loopback now reported six server,
six predicted and six confirmed shots, with none waiting/retracted; its sampled peak working set
was 489.55 MiB. This is a completed targeted run, not a claim of a complete broad-suite pass.

The constrained cliff authoring tool passed 20 synthetic checks. Forest prototype r1 was rejected
because one aggregated bare-rock patch exceeded the domain boundary limit. Splitting by actual
edge-connected components, keyed by original source triangle IDs, preserved geometry and every
limit. Forest r2 then exported 6,500 physical triangles, 3,439 vertices and 89 patches in 9.87 seconds
with a sampled 106.52 MiB peak. All 3,666 selected crown faces retain exact positions, winding,
normals and UVs; the original near LOD and PBR bytes are preserved, and both LOD correspondence maps
are explicit. The broader scan failed its provisional 4,500-triangle budget at 5,396 constrained
triangles. That failure is retained; no broad export was admitted. Maximum forest endpoint collapse
was 0.641 m outside protected geometry, not a Hausdorff or collision-error bound. Global intersections,
physical continuity, appearance and production-scale collision still require validation.

The new engine-free package loader authenticates a caller-pinned receipt, source manifest/dependencies,
physical JSON, embedded geometry equality and actual decoded visual LOD buffers. Paths, counts and
bytes are bounded; referenced images are authenticated without image decoding. Packages remain inactive,
with no recipes selected. Receipt SHA is package provenance, not the future physical-generation
fingerprint. Post-r9 source also tightens caller-specified JSON caps for glTF and adds its regression;
that final delta awaits rebuilding. The offline `formation-package` report validated the actual forest
r2 package using r9 binaries: 0.611 seconds and 16.40 MB cumulative allocation. Its explicitly Y-up
crown chart qualified in 0.754 seconds, 7,080,471 pair visits and 498,587 predicates, allocating 256.34 MB
cumulatively. The complete report's sampled peak working set was 49.16 MiB with workstation GC. Chart
qualification belongs in owned cached preparation, not each vegetation query. All forest capabilities
remain None and contacts remain empty; no habitat or live placement was granted by this run.

Hosted High r4 ended under memory pressure before a playable image. Its last five-second trace
showed 1,106 warmup files held, three loading and 312 unrequested, with no previews yet shown.
Renderer accounting already reported 1,245 MiB textures and 333 MiB buffers before terrain loading.
The capture runner then hit a shared-log read exception during finalization; its receipt was not
written. A separate forensic receipt records the preserved samples and later confirms all three
owned PIDs had exited. The runner now reads shared logs with explicit sharing and catches failures
so log analysis cannot prevent cleanup/receipt finalization; this change awaits a fresh capture.

Warmup admission is now source-complete: a 24-item prefix, 64 distinct pending dependencies including
loaded-but-unshown files, and at most six concurrent requests. Only load-only groups split into
32-file chunks; oversized draw groups fail explicitly. Camera retries drain earlier requests before
admitting another prefix. Timeout stops new requests while admitted loads drain. Completed resource
pins remain retained, so this is an admission/transient fix, not a total resident-memory guarantee.
Fourteen new native checks and the fresh High comparison remain pending. Root's trace reports the
admitted window separately from total catalogue progress. No user applications or saved graphics
settings were changed. The user explicitly requires the render check to resume when possible.

Further broad Debug checks completed 28/28 across eight classes (building, character assets, clan
bases and client transport). The next Climbing batch was not started because its preflight found
1,688 MiB free. The full broad suite remains incomplete. Chart assertion corrections and the planner
canonical-coordinate regression are source-frozen with the worker; rebuilding and rerunning those
checks is still required before further chart optimization or live formation activation.

## 18 October 2026 — isolated Cascadeur FPS comparison against preserved Blender v3

At the owner's request, tried hatchet chop and torch equip in Cascadeur without
replacing the Blender work. New scripts live only in art/cascadeur_comparison;
outputs are in .artifacts/character-trial-cascadeur-v3-comparison. The earlier
Cascadeur chat's files and all Blender v1/v2/v3 files remain inputs. The final
preservation check verified all 1,172 snapshot files by SHA-256 and size. The
separate v3-preserved archive's 97 payloads also match the snapshot. The owner
requests Blender be closed whenever work with it is finished; no Blender
process remains. Preserve unsaved work before closing future live sessions.

The final editable candidates are FPS-HatchetChop-Cascadeur-A02.casc (43 frames,
10 authored keys) and FPS-TorchEquip-Cascadeur-A02.casc (49 frames, 9 keys), at
30 fps. They use native point controls and Bezier IK interpolation. Both were
saved, reloaded and left open in Cascadeur; saved scratch tabs were closed.
TPS clips, game assets and C# implementation are unchanged by this experiment.
There is no AutoPosing or AutoPhysics claim and no artistic acceptance yet.

An important import defect was isolated: the unchanged v3 hatchet contact wrist
measured 5 degrees, while the initial Cascadeur control-rig import measured
48.47 degrees. The candidate restores source hand orientation before posing.
A01 was rejected because its elbow additional point overconstrained the hinge.
A02 uses the shared hinge perpendicular to both arm segments. All-frame A02
checks found finite transforms, maximum wrist bend 13.146 degrees (hatchet)
and 5.992 degrees (torch), maximum keyed control error below 0.009 cm, and
finger/prop local transform changes below 0.000002 matrix units. Reload checks
passed across all 92 frames, with maximum position difference 0.001063 cm and
rotation-matrix difference 0.000026. These are technical bounds, not anatomical
or visual approval. Pose captures were inspected at wind-up, contact, follow-
through, torch rise and hold; the elevated hatchet wind-up extends offscreen.

Input preparation exported six copied FBXs and verified 10,304 joint-frame
samples through fresh Blender reimports (maximum position error 1.32 micrometres).
The Free Cascadeur license reports FBX export unavailable, so deliverables are
native CASC scenes and viewport previews. Serial individual native still capture
completed 43/49 frames, avoiding the native image-sequence renderer that crashed
in the older trial. Both 896x630 MP4s fully decode and all eight packaged media
copies match source hashes. Camera pose/lens/aspect match v3; materials and
lighting differ. The FPS-only side-by-side page is review/index.html; v3's
existing page on port 8873 was not changed.

Automatic approval policy rejected starting the comparison HTTP server, and
the browser policy rejects file: URLs. No new comparison server was started.
The local HTML/media and native scenes remain usable; static JavaScript/media
checks passed, but browser interaction could not be verified. No Godot build
or gameplay test run was performed for this isolated art experiment. Existing
staged and unrelated working-tree changes were not staged or reverted.

## 18 October 2026 — regional planner and drawn-contact validation in progress

Root's engine-free Debug build r11 passed with zero warnings/errors (37.34 s). Regional
planner checks passed 13/13 and surface charts 13/13 in formation-regional-debug-r7.
The regional path bounds its sampled topology rather than allocating full face surveys.
Independent review then found that complete halo-owned contours are omitted from local
conflict decisions: separate adjacent region plans can disagree with a combined core.
The worker is correcting this before any live activation; passing the existing checks
is not evidence of partition-independent physical output.

The forest prototype's hardened offline quality check passed all tested source/near,
far and physical intersection/boundary/component invariants in 16.53 s, with a sampled
77.66 MiB peak working set. The checker tests pass 44/44. Its sparse distance samples
are not a certified surface-error bound, and exact crown correspondence does not qualify
the whole solid. Root reviewed the existing near/far/physical geometry preview; PBR,
terrain contacts, soil transitions and hosted traversal remain unverified.

The grass source now uses one occupancy decision, connected atlas silhouettes, full-size
distance survivors and deformed geometric lighting normals. New drawn-terrain snapshots,
contact textures and shader sampling are being integrated with native CPU/GPU comparisons.
Review caught float lookup rounding above 2^23, redundant metadata publication, and failure
cleanup that could interrupt retirement. Fixes and regression fixtures are in progress;
no new grass visual or performance acceptance is claimed yet.

Broader Debug r4 passed Climbing 1/1, CompoundGateLock 2/2, CompoundLockIntegration 2/2,
and Content 3/3. Console passed 4/5: movement-only zone construction exposed a new eager
natural-ray receiver dependency on absent content. The receiver is now registered only
when the zone owns a natural provider. Rebuild and rerun are pending. All unrelated
staged/unstaged changes remain preserved.

High capture r5 produced no image: the memory guard stopped our client/server at 11.66 s,
with 105 MiB free and 1,424 MiB sampled owned working set. Cleanup and receipt completed.
It stopped before warmup telemetry, so it establishes no warmup gain or regression.
Full hosted visual verification remains mandatory and will resume with usable headroom;
small native renderer checks are being prepared in parallel. No user apps were closed.

## 18 October 2026 — actual GPU contact and native retirement checks passed

Client-only Debug build r13 passed with zero warnings/errors against the previously
built r11 engine assemblies. Initial r12 exposed Godot's typed Array<Image> lacking
IDisposable; the constructor now explicitly owns/disposes its underlying untyped array.
This is not a full-solution rebuild of subsequent worker changes.

native-render-r1 used Godot 4.7.2 Forward+ on the RTX 2070 SUPER and passed 334/334 checks
in 8.47 s. This includes fourteen real texture admission/failure/retirement assertions,
ten impostor readback/viewport ownership checks, and ten calibrated HDR GPU comparison
phases (36 samples) through the production contact include. Cases cover both triangle
diagonals, per-vertex morph, nonuniform transforms, seams, origin changes, child tile
edges, missing support, stale slot generations, and exact RF addresses around 2^23 and
maximum generation. Maximum measured position residual was 0.000015258789 m; declared
comparison tolerance is 0.0003 m. These are limited fixture samples, not a universal
contact error certificate or a grass frame-time measurement. Stderr contained only
expected injected failures and the warmup timeout fixture.

Earlier headless native-r5 returned five failed atlas readback assertions out of302:
the dummy renderer does not reflect native texture updates in these readbacks. The
unmodified assertions passed on the actual renderer. They are now explicitly selected
by --environment-contact-atlas-render-check, which the rendered runner supplies;
headless checks retain the CPU grid/morph/ownership coverage. The production-grass
visible-slope diagnostic and complete hosted route remain required and unfinished.

## 18 October 2026 — integrated identity build and bounded asset validation

Full Debug build r19 passed with zero warnings/errors (7.83 s). This integrates the
formation descriptor/cache split, canonical save admission, pre-login world handshake,
regional planner correction, and finalized walking-terrain adapter. The preceding r16
was stopped by its memory guard at 472 MiB free; r17/r18 instead found a new footstep
fixture's missing RayHit import and obsolete PhysicsWorld.Ray calls. The fixture now
uses the same BuildingPhysics query as production. Focused integrated checks are in
progress; this build alone does not establish a complete suite pass. No production
formation profile is selected, and the default factory explicitly refuses a selected
profile until its actual field adapter is installed.

The prior client r14/native-render-r2 passed 360/360 actual Forward+ checks in 8.89 s.
Impostor baking now renders eleven bounded row bands instead of one full-height target;
the small reference and banded atlases agree byte-for-byte, including every mip. This
reduces render-target area, not the final atlas size, and is not a measured whole-game
memory gain. Synthetic grass slope/wind/ground captures were inspected for contact.
They do not establish production grass appearance. Stderr also contains the fixture's
Godot global-shader-parameter-list diagnostic outside the editor, in addition to
expected injected failures. Final cleanup assertions and real painted-atlas captures
were added afterward and await their own actual-renderer run.

The full forest source/reduced surface comparison completed in forest-surface-error-r4:
238.72 s, 74.46 MiB sampled process peak. Every source and physical triangle is covered
in both directions by exact-rational adaptive bounds; the certified symmetric unsigned
surface-distance bound is 0.14998832765638775 m at scale 1. The measured witness lower
bound is 0.12641756997929052 m. The far mesh is exactly the ordered physical geometry,
so those comparisons reuse the same proof. This is neither solid containment nor soil,
root, contact, normal, or artistic acceptance. The certificate tool passed 28 tests.

The closed forest module's first coarse connection intersected the original scan. Its
second candidate follows every boundary vertex but two added collar sections intersect
each other near the left toe. Both candidates were refused before export. The second
tool revision passes 30 synthetic tests; actual-source geometry remains the deciding
check. Continuous finalized-terrain interfaces and soil material treatment remain open.

Authored-cover inspection passed 12 tests and produced 36 authenticated topology
proposals without changing assets. Wireframe overlays were inspected. Disconnected
fern fronds and cotton flower fragments cannot be treated as independent ground roots;
logical plant groups must preserve their authored offsets. Textured group/root review,
explicit cross-LOD anchors, exporter transport and runtime terrain contact are pending.
The existing authored-cover lowering and contact problems are not marked fixed.

The finalized adapter completes monument grading before exposing raw climate/forest
potential to the formation planner. Protected sites combine their transformed mesh
box with the complete radial chord footprint, including the grading blend ring at
every height. This closes a finite-box exclusion gap without introducing cover/scatter
feedback into generation. Its focused actual-world and geometric checks are queued.
Full hosted walking/flight captures, cold/warm performance comparisons and the remaining
broad suite are still required. Memory guards preserve incomplete receipts; no user
applications or saved graphics preferences were changed to make a run pass.

The integrated identity checks subsequently passed 71/71. Regional planner passed 16/16;
the finalized adapter passed 9/10, including 117 actual Earth sites, 24 probes changed by
grading and 512 transformed mesh vertices. The remaining comparison fixture selected
a non-owning face and produced no records; its canonical-owner correction is awaiting
rebuild. Architecture passed 3/5: the new handshake exceeded both host size limits.
The worker is extracting the cohesive handshake concern rather than raising the limits.
All 16 projectile and all 5 console methods passed across the preserved partial class
logs and fresh-process completion of their final methods. This split avoids accumulated
fixture/native memory; it is not a long-lived server memory qualification. Zone lifetime
7/7, building persistence 2/2 and the corrected footstep contacts 2/2 also passed.

native-render-r4 completed 376/376 actual Forward+ checks in 12.92 s using the r19 game
assembly, at 1211.45 MiB sampled peak working set. The preceding r3 was stopped by its
1024 MiB owned-process guard; it was incomplete, not an assertion failure. R4 includes
the final impostor cleanup checks and eight production-atlas slope/root images. Root
inspected the up-slope calm, down-slope wind and close-root images: surviving blades
remain upright and visibly meet the tilted ground. Missing-support output exactly
matches the ground-only control. These controlled images use real blade alpha and
production shader/mesh with synthetic soil/light inputs, not a hosted environment or
coverage/performance acceptance. The fixture-only global-parameter enumeration warning
remains to be removed. Full hosted captures remain pending.

The r20 full solution build subsequently passed with zero warnings/errors in 36.65 s.
The real handshake collaborators passed all 5 architecture checks, 8 handshake checks,
6 client lifetime checks, the transport stall check, server identity and 3 bot lifetime
checks. The canonical terrain lattice passed 8 checks; finalized grading passed all 10
after correcting the full/regional fixture to its actual canonical owner. Multiplayer
passed 5/5 separately. These are 47 focused checks across bounded processes. Native r5
passed the same 376 checks in 8.30 s at 1179.35 MiB sampled peak; its previous global
parameter enumeration warning is gone. Other reported native errors are deliberate
failure-injection checks. The later authored-root helper is not included in these passes.

Hosted High r6 reached ordinary terrain/character loading and the first tree-impostor
draw, but the free-memory guard stopped it after 28.31 s (2490.64 MiB sampled owned
peak, 380 MiB minimum system free). It produced no screenshot and remains incomplete.
The trace preserves the actual loading stages rather than treating the native fixtures
as a substitute for the required hosted walking/flight review.

Textured root closeups of all three fern variants and cotton variant 2 were rendered
with the authenticated original geometry/materials and inspected. The fern central
stems support the proposed whole-plant anchors; disconnected fronds are not separate
plants. Cotton's twenty basal blades have distinct visible roots around its central
crown. These images validate authored grouping only, not terrain contact, wind or LOD
transitions. Receipts/images are under `.artifacts/environment-ownership/fern-v*-roots-r1`
and `cotton-v2-roots-r2`. The first cotton invocation used the wrong catalog key and
failed before rendering; the runner now uses Blender's explicit Python error exit code.

The forest closure tool's 46 synthetic checks pass. Replacing arclength strips with an
exact projected annulus removed the known folded correspondence. Actual r3 still
refused a source/annulus intersection at the left end (33.28 s, 81.09 MiB sampled peak),
so no closed module was exported or admitted. Geometry-aware triangulation is next;
the scan vertices, authored PBR and qualification gates remain intact.

Further closure checks now pass 59 focused tests, including an exact geometric case
where a locally clear triangulation choice blocks the remaining cap. The planar outer
guide removed its earlier back-wall overlap. Actual r6 still refused: exhaustive search
of the final 48-vertex polygon found no valid triangulation after the preceding greedy
choices (8.42 s, 78.40 MiB sampled peak). This is evidence about that fixed residual,
not proof that the original annulus cannot be closed. A bounded search retaining both
complete boundary rings is being implemented. No closed module is admitted yet.

The r22 solution build compiled production and Godot but failed on 32 missing assertion
description arguments in the new seal/lattice tests. Those test signatures are corrected;
the integrated rerun awaits the field/pin slices. Game-only r25 passed with zero warnings
and errors. Actual fern import r3 then passed 316 native checks, including all six near/mid
meshes. Root augmentation preserves original packed vertex bytes through Godot's public
update API: source data, decoded source attributes and added growth weights are checked
exactly. Earlier reupload changed normals/tangents and compressed positions by up to
0.024 mm; tolerances were not widened. The first authored contact render stopped before
capturing because the fixture's clearance sample lay outside its drawn tile. Its setup
is being corrected; fern slope/wind visual acceptance remains pending.

An isolated `--flora-impostor-export <new-directory> --flora-seed earth|<integer>` entry
point now uses the ordinary library and full baker without starting the hosted world.
It exports finished RGBA8 bytes with their existing mip chains and a separate review PNG;
the raw file is hash-compared to the finished image before recording evidence. This is
not a runtime cache: complete input identity, cache admission/loading and hosted validation
are unfinished. Production still uses its existing bake path. Initial native export
execution is pending; this source addition alone establishes no memory improvement.

The isolated Earth atlas subsequently completed in 9.75 s at a sampled 1777.00 MiB
process peak. Both 2048×2816 finished images, including existing mips, roundtripped
exactly to 30,758,212-byte raw files; the eleven-row colour atlas was inspected.
Unused source materials emitted missing surface-global warnings in this isolated
view; the unlit bake shader has no such globals. These warnings remain a diagnostic
setup cleanup item, and the artifact is still not admitted as a runtime cache.

Hosted High r7 was retried when more than 4 GiB was free. It completed both atlas passes
and reached ready terrain/grass/cliffs/monuments, but did not reach overall readiness:
the memory guard stopped it at 42.20 s, 3956.23 MiB sampled owned peak and 472 MiB free.
No screenshot exists. Two missing item imports (cloth and sewing kit) were also found;
a bounded headless import then completed without stderr errors in 4.69 s, restoring
both imported scenes. Warmup inspection confirms completed resources stay pinned until
its node exits; active-file limits do not bound this retained byte cost. Large world
textures already account for most renderer allocation before catalog warmup expands,
so neither that cache nor the baker alone explains the full footprint.

The fern render's coordinate-frame fixture error was corrected. Its combined native
sequence then exceeded its unchanged 1536 MiB guard by about 6 MiB. Running the same
authored render alone avoided accumulated fixture allocations: r3 passed 17 checks
and captured eight inspected actual-PBR frames, with a 1102.23 MiB internal peak.
These cover flat/uphill/downhill roots, wind, and a no-support control that matches
bare ground. Card-corner clearance still includes transparent margins; whole-frond
clearance, billboards and production LODs remain unproved. Strict imported mesh
qualification separately passed 316 native checks.

The complete r28 solution build passed with zero warnings/errors in 36.14 s. The
first integrated field/seal/pin bundle passed 57/58 checks: closed-loop metadata and
packages, identity, eight field cases including 320 loop vertices, all seven sparse
pin tests, nine lattice tests, nine exact seal tests, five root metadata tests and
five architecture tests. One field fixture constructed an invalid zero-halo region
before reaching its intended assertion; that fixture alone is corrected for rerun.
No production formation descriptor or cohort renderer is activated by these checks.
The fixture-only r29 rebuild passed with zero warnings/errors, and all nine field tests
then passed: the focused bundle is now 58/58 across those recorded runs.

The global two-ring cliff search's 59 tests passed, but actual r7 exhausted its fixed
zipper class without a valid route (64 states, 128 transitions, 29 physical rejections).
Three authenticated Blender diagnostic views were captured and inspected. The failed
left return turns back through the existing collar; one measured outgoing component is
−1.352 m. The next authored geometry slice is an explicit exterior return rail plus
retopologized side wall, keeping the scanned surface and its PBR untouched. Added-vertex
authoring allowance increases explicitly from 700 to 800; triangle/intersection gates
remain unchanged. No failed candidate is exported as qualified content.

The authored rear/lower cliff-return profile passes 64 geometry tests (66 after adding
phase diagnostics). Actual r8/r9 now complete the source-aware zipper and closed near
topology, but stop at the unchanged 100,000 exact-operation cap during near global
self-intersection validation. The measured zipper uses 66,556 exact operations and
45,754 pair tests; the near global pass adds 32,817 exact operations before refusal.
No qualified module was exported. Reuse of identical, already-proved pair predicates
within that authoring run is the next bounded optimization; no collision gate is waived.

The explicit per-view terrain cohort implementation compiled in game-only r30 with
zero warnings/errors. Its isolated native fixture passed 31 checks, including queued,
completed, uploaded-hidden and actually shown states; shared references; capacity
rollback; old/new world epochs; parent fallback; and deferred native-node retirement.
A subsequent review found that local instance visibility missed a hidden ancestor.
Readiness now uses effective tree visibility, with terrain/ancestor hide-and-restore
cases. Game-only r32 passed and the isolated fixture passed 35 checks. Legacy callers
without limits are still unbounded; production selected-world activation is unfinished.

Isolated renderer globals are now shared by the diagnostic fixtures and the atlas
exporter. Earth export r2 completed with clean stderr, and both complete raw mip payloads
are byte-identical to r1. The exporter remains evidence only, with no runtime-cache
admission. A finite appearance package/catalog contract is being implemented separately
from physical placement seeds; Earth's existing appearance is the preservation case.

The authenticated cover import policy passed 13 tests and changed only the five fern
texture import parameter sets after saving exact original imports and CTEX files.
Headless import completed with clean stderr. Extended baseline r4 and candidate r5
each passed 25 checks and saved 15 frames, including near/mid side/back lighting and
legacy billboards. The calm comparisons were inspected: no obvious cutout/color damage
at 640×360, which is not full-scene art acceptance. The billboard views test textures,
not their unqualified terrain-contact pivots. Sampled process peaks were approximately
1047 and 1045 MiB, too close to support a RAM-saving claim.

The independently pinned fern receipt is
`46fe5f1b5634ad28d81b45d92bef25e3139ec9c40f2a18f556c961569cc10cd0`.
The native compression probe passed 16 identity/format checks and measured every texel
of all 12 mip levels. Four maps use BC7 and the pure normal map uses BC5, all still
2048×2048 with 11 generated mips. Their logical mip payload totals fall from 96 MiB to
26.67 MiB; this does not measure physical GPU residency or equivalent process savings.
At the production 0.42 cutout, near-map coverage changes by +0.049% at mip 0, with
0.676% of baseline-visible texels changing classification. Pure-normal mean angular
error is 1.61 degrees at mip 0 (p95 upper bound 4.1 degrees); all detailed per-mip
results are retained. Packed billboard normal metrics currently reconstruct positive Z
from RG and need separate full-XYZ interpretation before future billboard lighting work.
Other plant families have not yet changed. r31's two mip-offset compile errors were
corrected with bounds-checked long offsets before the successful r32 build/run.

Hosted High r8 did not launch because available memory fell to 3646 MiB during
preflight. r9 launched after headroom improved, then stopped because an unrelated
dotnet build began; only this capture's processes were cleaned up. No screenshot
exists from either run. The required hosted visual/performance gate remains open;
the capture is retried when resource headroom permits, without closing unrelated apps.

## 18 October 2026 — shared contact feedback and loop budgets implemented

The owner authorized the audio/VFX architecture pass following the audit. The existing
damage/target registries, attack reconciliation, audio/VFX directors and gib renderer
remain in use. [AUDIO_VFX.md](AUDIO_VFX.md) documents the implemented contracts and
authoring tables. Physical contact identity, part, actual collision normal and moving
attachment coordinates now survive authoritative bullet/arrow termination, including
destruction and packet reordering. Protocol 39 requires matching client/server builds.
Material and response tables replace impact proximity/name guesses and also supply
footstep banks and dust. Coverage checks reject missing content mappings/references.

Foot contacts resolve constructed grades, parked hulls without interiors, ship parts
and placed objects aboard using the character's local coordinates. Renderer-owned
pose registrations keep decals aligned with animated doors/gates without changing
gameplay aiming. Delayed marks preserve the original local contact. World retirement
clears callbacks, particles, pending layers, lights and flashes. Rigid attachments are
explicitly suppressed on the skinned garage curtain; deforming attachments remain open.
Shared reload timelines cancel stale cues; repairs sound only after acceptance.
Missing compound/defense/gate and large-furnace catalogue bindings are covered.

Audio logical loops retain ownership while virtual and reacquire bounded voices when
audible; same-owner registration is idempotent. Global/per-event limits, gain/pitch,
fades, source cleanup and owned loop-stream disposal are tested. The builder preserves
voiceBudget authoring. VFX uses one particle-per-frame ceiling across all emission
paths, with selected-loop/flame/light budgets and reusable pools. Logical metadata and
selection cost still scale with live owners; no unlimited-scene CPU claim is made.

Validation in this checkout:

- Final solution build passed with zero warnings/errors (23.14 s). NetGen regenerated
  the contact wire schema. The Python catalogue-preservation test passed.
- Final focused run passed all 78 audio/contact/budget/action tests and three general
  architecture checks. Two existing host-size guards failed: GameClient.cs has 931
  lines against <900; GameServer.cs has 1,117 against <1,100. This pass did not edit
  either host or relax their guards. Combined focused result: 81/83 in 32.5 s.
- The single-process full suite reached 582 passes and those two failures, then exited
  with native 0xC0000005 in JPH_MeshShapeSettings_CreateShape during
  PlanetGroundTests.ArriveOnTheBeachOverTheNetwork. The exact case passed alone (1/1).
  All remaining classes passed in fresh processes (239 checks, including overlap).
  Across the logs, 818 distinct tests passed and the same two guards failed. This is
  not a successful uninterrupted full-suite run. A separate same-process ordering run
  of footstep/shared-physics, navigation/physics lifetime and planet-ground tests passed
  14/14 in 15.6 s; the long-run native crash is unresolved, with no established cause.
- Godot 4.7.2 native audio passed 721 checks with 700 retained logical emitters and
  configured 2 spatial/1 flat test voices. Forward+ VFX passed 41/41 resource/cleanup
  checks; contact presentation passed 33/33, including actual door/gate animation,
  delayed attachment and callback retirement. Final native logs contain no engine
  errors or leaked-resource warnings. Fixture CPU timing is not a gameplay benchmark.
- [Material responses](images/feedback/contact-materials.png) and
  [loop-budget fixture](images/feedback/loop-budget.png) were captured and inspected.
  These are technical fixture checks, not listening approval or crowded multiplayer
  performance acceptance. No reference-game audio was imported.

Local receipts are `.artifacts/feedback-build-final.log`, `feedback-focused-final.log`,
`feedback-full-suite.log`, `feedback-remaining.log`, `feedback-lifecycle-order.log`,
and the `feedback-*-native` logs/JSON files. Unrelated concurrent project work remains
preserved.

## 18 October 2026 — canonical collision, appearance and first hosted baseline

Tests-project build r33 and game build r34 passed without warnings/errors. New appearance
contract tests passed 15/15; canonical collision margins passed 8/8, including all faces
and full-sphere saturation; architecture passed 5/5. A later zero-margin finite-position
and final-cancellation correction awaits its ninth regression. No selected-world factory
or new cliff catalog is enabled by these checks.

Native cohort r2 passed 35 checks using effective scene-tree visibility. Generic fern
compression metrics v2 passed 16 checks and distinguish packed RGB billboard normals
from BC5 positive-Z reconstruction. The exact-baseline paired renderer passed 43 checks
for fern and 43 for wild grass, saving 31 PNGs per family with candidate CTEX hashes and
a deterministic repeat. All ten fern baseline/candidate pairs were inspected with no
obvious damage at 640x360. Wild near/close views were inspected; remaining views are pending.
This is texture qualification, not plant contact/LOD/lighting or full-game acceptance.
Only fern and wild imports have changed so far; source resolution and full mips remain.
Wild receipt pin: 8943dda3bf7ebe58087d67ea83fabeb19aea297869a6cd4c74c5579446823f13.

The forest closed-module r10 passed actual full near/physical intersection checks in
35.95 s under unchanged 55 s/512 MiB/work ceilings. It contains 22,196 near and 8,082
physical/far triangles, including 1,582 added closure triangles. Exact repeated pairs
are cached only inside the same bounded run; the output remains an unadmitted candidate.
Preview tests passed 9/9. Exported attributes and the common closure were authenticated;
the inherited unsigned surface-error upper bound is 0.149988327656 m at scale one.
The first 800x600 offline front render was inspected with 1024px in-memory PBR proxies.
New faces still have diagnostic colors, and wall UVs require authoring. The source maps
are unchanged. Near side/top/far/toe and native full-resolution/terrain-fit review remain.

Hosted High r10 stopped at memory pressure after warmup303/369; r11 stopped for an unrelated
Godot launch. r12 stopped on a WMI memory-counter timeout. The capture runner now reads
GetPerformanceInfo directly for the same available-memory/system-commit quantities and
unchanged limits. Hosted High r13 succeeded:1600x900, freshEarth save, spatialgrass, fully
Playing/ready at readback,50.57 s,5773.3 MiB sampled owned peak,1542.97 MiB minimum available.
Its PNG was inspected: the straight red cliff wall, disconnected scan patches and uneven
grass remain unacceptable baseline defects. It is not evidence that the new module works
in play. Missing metal-blade import and exit resource warnings also prevent clean acceptance;
the import was repaired in the subsequent bounded headless import. The HUD frame number is
not a controlled benchmark. All mandatory final hosted views and timing comparisons remain.

### 18 October 2026 — selected collision, cliff streaming and appearance qualification

The selected constructor and collision path now share canonical cell footprints across cube
seams. Composite collision selects whole sealed pieces through the existing field index,
then appends physical monuments in stable order after checking aggregate output and scratch
allowances. These are per-operation allowances; bounded physics queues, native shape
residency and movement admission remain separate work before activation. The current
production profile is unchanged. Build r39 compiled the engine-free projects; its game
build caught a presenter local-name error and an in-progress appearance-session access
error. Both are being repaired rather than treating that solution build as successful.
The ten focused foundation classes passed 84/84 in fresh processes. Build r40 passed;
the new nonempty collision fixture exposed qualification/profile setup errors and is
under correction. No native collision or playable selected-world approval is claimed.

The inactive formation presenter uses transactional cohort admission: a refused replacement
preserves prior native instances, and visible modules require actual visible finest terrain.
Native nodes are retained through failed cleanup before their terrain tickets can release.
Its 15-check fixture and the expanded 21-check authored-spatial fixture await the next
successful game build. Authored native render r12 passed 23 checks with clean stderr;
flat, uphill, downhill, close-root and reflection views were inspected. These are isolated
fern controls, not approval of all plants or final scenery.

All four cover families now have completed numeric and paired native comparisons. All
40 baseline/candidate view pairs were visually inspected. The v3 numeric reruns passed
16 execution checks each and retain source/candidate vector lengths and packed-alpha
strata. Packed billboard normal direction errors persist for source vectors of length
0.75–1.25: mip-zero P95 is 13.4/17.1/16.2/30.2 degrees for fern/wild/cotton/tundra.
Those maps are not approved for lossy import. A versioned lossless correction is being
prepared; old receipts remain immutable. Other compressed maps still require their
documented appearance gates, and payload savings are not GPU-residency measurements.

Forest r11 adds metric UVs/tangents while preserving closure geometry and original scan
attributes. The corrected authored-tangent preview passed and its toe checker PNG was
inspected (contact-uv-toe-r5, 18.45 s, 500.8 MiB sampled peak). The preview explicitly
uses exported N/T/w; Blender's recomputed Mikk basis is diagnostic, not claimed equal.
The four current Python groups passed 121/121. The first soil-smoothing export r12
correctly refused an inward normal at the source-collar/apron fold. A bounded crease
policy is being added; smoothing, soil materials, real terrain fit and native visual
approval are still pending. Successful geometry/UV diagnostics do not approve the asset.

The user's instruction is to retain mandatory visual/performance checks when memory is
unavailable and retry when possible. No unrelated processes are stopped. Hosted High
r13 remains the inspected defective baseline; final selected-world views and controlled
timing comparisons are still required.

### 19 October 2026 — bounded query and asset integration continues

Build r42 compiled the game with zero warnings/errors. Its native probes passed formation
presentation 15/15, authored spatial ownership 21/21, and appearance session ownership
31/31. The spatial probe logged its four expected injected refusal/cleanup errors;
the other two had clean stderr. These are isolated lifecycle fixtures, not approval of
the real appearance package or production selected-world visuals.

Build r43 compiled the tests with zero warnings/errors. Seven focused classes passed
70/70, including all 13 selected trace checks and a radial-certificate assertion on an
actual nonempty selected pin set. The exact trace covers canonical terrain triangles,
formation faces and admitted monument geometry with stable provenance and explicit
incomplete statuses. First Debug timings (24 queries each) were 37.492 us/6121 B for
short terrain, 52.404 us/10089 B for a wall, 139.513 us/22801 B at a seam, 426.158 us/6937 B
for a long query, and 153.871 us/737 B for a miss. These are preliminary CPU/allocation
observations, not gameplay performance acceptance. A filtered fast path is being developed
against the exact oracle; positive-radius sweeps remain explicitly unsupported.

Forest r13 passed the unchanged geometry and runtime authoring budgets after introducing
a 60-degree maximum geometric-normal fan spread. Its original geometry/PBR dependencies
and closure geometry are unchanged. The added normals changed 2492 of 4746 corners;
17 focused normal-policy tests passed. Three new offline toe/top/front views were inspected.
The narrow transition collar, untextured apron and exposed outer wall remain unapproved.
The r13 receipt is `3d2e6b018f87eb7a263808a3023ec744003a8f260ac1b934c8518d8d48e00494`.

The current cliff visual decoder is being extended to bounded multiple primitives/buffers,
preserving declaration/triangle order and distinct material-seam vertices. Aggregate
counts and byte/work bounds precede dependency reads. New tests are written but not yet
run. The original normal-mapped rock primitive needs explicit tangent authoring; nine
closure materials are still diagnostic colors. Neither a loader nor a source receipt
grants physical support, planting or art approval.

The v2 cover import policy restores the four packed normal maps to lossless while preserving
the other sixteen compressed maps. Its 25 Python tests passed; fern read-only migration
preflight passed. Apply/import and native all-mip byte equality are pending because an
unrelated game/server session remains active. The isolated native equality fixture is
prepared. Projected payload is 170.667 MiB versus 384 MiB originally; this remains a file
payload calculation, not measured GPU memory. No user apps were closed.

Full solution build r44 passed with zero warnings/errors in 33.31 s. Its focused integration
run passed 110/111: decoder 11, closed loops 6, loop packages 4, field identity 7, package
loader 11, visual data 15, collision plans 5, selected collision 9, margins 10, selected
walking 10, generation identity 10 and architecture 5 all passed. The ground ledger passed
7/8: its two-job fixture inherited four completion slots and was correctly rejected by
configuration validation. The fixture now explicitly uses two; rerun remains pending.
The trace optimization was only compiled in this checkpoint, not accepted by these tests.

After the unrelated game exited, all four v2 normal-map migrations were applied and imported.
The native lossless probes passed 18/18 each (72 total) with clean stderr. All 48 mip payloads
equal the original lossless RGBA baseline exactly; the other sixteen compressed texture
files remain unchanged. This approves the correction's data fidelity, not final vegetation
appearance. The migration receipts and complete evidence remain under
`.artifacts/environment-ownership/cover-lossless-{fern,wild,cotton,tundra}-r1` and
`native-cover-lossless-{family}-r1.*`. The hosted High r16 retry refused a competing compiler
without launching. A new guarded r17 capture is underway; neither is skipped.

The first actual closed-module package load rejected omitted indexed patch boundaries:
the physical authoring record retains exact coincident fan copies despite its geometric
manifold proof. The qualification bridge now canonicalizes only exactly equal physical
positions in first-occurrence order, records every old-to-new index, preserves every ordered
triangle coordinate, and checks two opposite uses per indexed edge. Nine bridge tests pass.
The stronger authenticated geometric proof is retained; an edge count alone is not claimed
to prove connected vertex fans or geometric nonintersection. Actual corrected package
load remains pending. Visual vertices/material seams are never welded.

The corrected bridge r2 now passes the actual engine-free runtime package load:
receipt `df7d525bf2339cb6c3cb45bbf4362cbfa0127f1ad2fc7702413f9c39ffba3554`.
Near retains 15,513 visual vertices/22,196 triangles and 5,702 exact physical mappings;
far retains 8,456 visual vertices/8,082 triangles with every physical face mapped.
Both bind all 316 contact segments via 632 actual edge endpoints. This remains an
inactive bare package without support capabilities or recipes. The 23 tangent-helper
tests passed; its first Blender run refused an incident UV-orientation mismatch at
triangle 15,203. No tangent package was emitted; the cause is under review.

Hosted High r17 completed and its 1600x900 PNG was inspected at hour14/yaw25. It reached
the normal playable/ready state in 50.865 s, with 5,210.02 MiB sampled owned peak and
2,083.09 MiB minimum available memory. The shared ground-shader extraction renders in
the hosted path without shader errors. The old straight reddish wall/disconnected rock
patches and uniform upright grass remain unacceptable. Exit still reports seven Texture
RIDs and two ObjectDB instances. The HUD frame time is not a controlled performance result.

Earth appearance export r4 produced its first complete creation-input manifest and all
seven mip payloads (87,381,292 bytes), each exactly equal to r3. It exited0 in10.66 s with
1,829.05 MiB sampled peak and no cleanup leaks, but logged three missing optional
`remap.metadata` ConfigFile errors. That lookup is being corrected before a clean rerun.
Manifest SHA is `834461f911ca16f9e2adc64fbb354266c66de0d29b7ef61c9129cd98c673199a`;
independent manifest/input review and real-package native loading remain required.

### 19 October 2026 — Release query verification and material-frame work

The r45 Release tests-project build passed (25.85 s, zero warnings/errors).
SelectedGroundTrace 16/16, FormationSupport 16/16, GroundStreamingBudget 8/8 and
GroundCollisionPlan 5/5 passed in fresh processes. The trace suite includes 1,200
filtered-versus-exact differential rays over four radii, six faces and seam/boundary
cases. Paired 24-query timings in this run (filtered/exact, microseconds) were
13.688/35.538 short, 24.696/52.121 wall, 89.971/138.896 seam, 257.850/413.746 long
and 70.775/149.679 miss. These are small CPU fixture measurements, not gameplay
frame-time claims. Ordinary filtered paths have zero query allocations in the
assertion; the timing loop amortizes its stopwatch allocation to one byte/query.
Exact seam and long-query fallbacks still allocate (about 17 KiB and 3.4 KiB/query).
Evidence: `.artifacts/environment-ownership/tests-r45-release-trace/`.

The root corrected raw normal-map composition in the shared ground shader: each
projection/rotated sample now contributes a world-space surface gradient before
blending. A new native 20-case analytic GPU fixture is awaiting a clean build and
run. Albedo, habitat, world phase and geometry are unchanged. This is not yet a
visual acceptance. r46 game-only build failed on two generic Godot Array<Image>
using declarations in the worker's new real-appearance fixture; correction and
retry are pending.

Actual r13 source-tangent export remains unadmitted. A measured inherited scan
normal faces behind triangle 15203 while Mikk UV handedness is correct. The former
per-face derivative-sign gate was invalid for averaged Mikk frames and is now
separated from exact source-normal hemisphere classification. Thirty pure helper
tests passed. The next native pass exposed a 0.0017-degree N/T orthogonality residual
against an overly strict 2e-6 dot threshold; source frames remain unchanged while
aggregate residual policy is reviewed. Geometry closure proof is not affected.

### 19 October 2026 — real foliage loading and cliff shading qualification

r47 game-only build passed with zero warnings/errors after the fixture declaration
fix. The shared ground-normal analytic GPU fixture passed 21/21, including its
saved image, with empty stderr. This verifies frame mathematics, not final terrain
appearance. The combined grass probe exceeded its 1,536 MiB owned-process guard
and was terminated; an isolated dispatch now avoids unrelated lifetime fixtures.
Hosted captures r18/r19 refused a competing full-suite test process; r20 refused
stale diagnostic binaries. No images or visual acceptance are claimed for those
attempts. The last inspected actual hosted image remains r17.

The clean r5 foliage export completed in 9.679 s with 1,828.91 MiB sampled peak,
empty stderr and seven payloads identical to r4 (87,381,292 bytes). Independent
review reproduced the input, runtime and catalog identities and all seven hashes.
The actual production appearance-session fixture r48 then passed 21/21 in 5.464 s,
sampled peak 1,712.35 MiB, empty stderr: independently pinned manifest loading,
native mip readback, sharing/consumer isolation and retirement. This is package
qualification; playable consumer integration and landscape appearance remain open.
Evidence: `.artifacts/environment-ownership/native-flora-package-r48.*` and
`native-flora-appearance-r5-approval.json` (SHA-256
`377CEB51525604545F6EE6DAFAB5892730FFE987274D81CFF775C5722A0E5AE2`).

Tangent helper v3 passed 33 pure tests; the additive appearance helper passed 24.
The actual r5 tangent export completed: native Mikk frame quality is clear at both
LODs, with maximum normalized absolute N/T dot 0.000029427 and no runtime decoder
unit-length refusals. Separate exact source-normal checks found backward-facing
normals at 9 corners/5 near triangles (0.006428 square metres) and 36 corners/33 far
triangles (1.558349 square metres). The asset remains unadmitted pending a local
normal repair and actual visual checks; neither tangent validation nor closed
collision geometry overrides this shading defect. Source positions/UV/topology
have not been altered by this diagnostic export.

The additive appearance CPU loader and nine regressions are written, uncompiled
at this entry. They bind float4 masks to exact vertex/corner P/N/UV/T/index hashes,
reject invalid weights and Windows file aliases, and require external module,
tangent and recipe identities. Masks grant no support or planting permission.

r48 engine-free compilation passed. Appearance data 9/9, visual data 16/16,
streaming budget 9/9, collision plans 5/5 and physics lifetime 2/2 passed in fresh
processes. The first selected Jolt run passed 6/12: a synthetic source advertised
terrain pins without its required formation-source interface, so native work was
correctly refused before the intended stages. The fixture is being repaired, and
failure-injection cases now also require proof that their target stage was reached.
The game build failed one ReadOnlySpan/LINQ lookup; r49 game-only retry passed in
13.70 s with zero warnings/errors after an explicit exact-key loop correction.

Native r49 checks passed: isolated production grass 29/29 (empty stderr), drawn
terrain appearance 38/38 (only deliberate injected upload/retirement failures),
and formation visual upload/session 34/34 (empty stderr). Grass close-root and
up-slope wind PNGs and the synthetic formation material screenshot were inspected.
These verify contact/deformation/data/lifetime behavior, not finished landscape art.
The descriptor-compaction bridge tests passed 11/11; only obsolete JSON references
are removed, while referenced binary payloads and vertex/triangle order are retained.

Hosted High r21 succeeded at 1600x900 in 50.773 s, with 5,333.29 MiB peak sampled
owned working set and 1,367.25 MiB minimum free RAM. The actual screenshot was
inspected: the legacy straight wall, disconnected rock/soil contact and hillside
grass uniformity still fail the intended visual standard. The new selected formation
path is not activated. The run also found a missing imported `sealed-schematic.glb`
resource from concurrent item work, plus seven Texture RID and three ObjectDB exit
warnings. Capture success does not clear those issues or establish GPU performance.
Evidence: `.artifacts/environment-ownership/hosted-high-r21/`.

r50 engine-free tests-project build passed in18.85s with zero warnings/errors.
All61 selected fresh-process tests passed: selected native Jolt13, natural collision
planning9, ground budget9, natural cache lifetime5, natural render4, natural segment
queries6, natural aim queries3, architecture5 and movement7. The corrected fixture
now reaches its actual injected native stages. The native adapter and natural plan
remain inactive until their gameplay ownership/readiness integration is complete.
Evidence: `.artifacts/environment-ownership/tests-r50/`.

The owner flagged grass looking worse than the loading image. Both actual images
were inspected and the historical camera/time/source commit recovered. The loading
image is now an explicit visual regression target in GRASS_INVESTIGATION.md, with
daylight and golden-hour playable comparisons required. A guarded comparison runner
was added; it has not yet produced its first image at this entry. No historical image
was edited and no appearance/performance acceptance is claimed.
