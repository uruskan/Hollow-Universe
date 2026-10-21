# Known problems and the plan for them (14 October 2026)

## The ground looked like tiles of mud under the grass — found and fixed 19 October 2026

The owner's captures (19 October) showed the meadow as half-metre squares of grass and bare mud, no detail under the
tufts, and a flat beach. The squares were the grass field's cells: each 0.46 m cell got its whole five-card tuft or
nothing, so at the meadow's density half the cells were solid grass and half bare. The ground under them was the
Dry Grassy Ground scan, which is mud with a little straw. Fixed: the tuft's cards thin one by one with the density
(`surface_grass.gdshader`), and the materials have alternative scans chosen per pixel (a lifted wet-grass scan under
lush grass, pebbly sand on beaches, eighteen owned 4K scans in all; docs/WALKING_GROUND.md §8). Open: a truly green
meadow floor needs a green grass-ground scan the library lacks at 4K; the alternatives' selectors and gains are first
settings. Status: fixed in the working tree, captures in `docs/images/ground/ground-textures/`; owner acceptance pending.

## Long full-suite native mesh-build crash — found and fixed 19 October 2026

Runs on 16 and 18 October exited with native `0xC0000005` in `JPH_MeshShapeSettings_CreateShape`
from `PhysicsWorld.BuildShape`, and once in `JPH_PhysicsSystem_Create`, always late in a long
process and never alone. Cause: JoltPhysicsSharp 2.23.0 creates physics systems, shape settings
and body-creation settings without ownership of their native handles, so `Dispose` released none
of them. Every world and ship space leaked its native system, bodies and shapes, about 16 MiB per
world with resident ground; the test process grew by about 16 MiB per test, to 9.4 GiB after 565
tests. Once Windows refused an allocation, Jolt dereferenced the null result. A per-process commit
cap reproduces the same stack on demand in the same beach-arrival test.

The fix (T0.3 of TERRAIN_SCENERY_PLAN.md): `JoltRuntime.HandOverToSystem` makes each physics
system own its handle and gives its three layer/filter objects to it, since joltc's system destroy
deletes them; `Owned()` makes every shape and body-creation settings object release on dispose.
Repeated worlds with ground now stay flat. `PhysicsLifetimeTests` checks the memory and
`ArchitectureTests.JoltNativeObjectsAreReleasedWhenDisposed` forbids unowned constructions. The
same leak affected the game: every terrain tile a server or client built stayed in memory.

## Concealment bushes absent from normal ground play — 18 October 2026

The owner reports missing bushes and requests hiding vegetation around monuments. Source
review confirms ordinary bushes exist in scatter/FloraLibrary, but GroundView's NaturePlants
enumerates only resource-mapped natural objects, excluding ordinary bushes. Monument placement
also clears vegetation across whole bounding circles. Foundation-driven foliage suppression is
not connected, and existing quality-dependent shrub LODs would produce unequal concealment if
enabled unchanged. [Investigation and proposed integration](TERRAIN_SCENERY_PLAN.md#concealment-bushes-investigation--18-october-2026)
records evidence, resource/scenery ID hazards, shared clearance and visibility requirements.
Status: open; source/published-reference review only, no implementation or new runtime validation.

## Grass remains sparse, clipped-looking and costly — 18 October 2026

The owner reports poor coverage, apparent burial and weak lighting/color variety.
[Current investigation](GRASS_INVESTIGATION.md) identifies fixed procedural instance
grids with shader-only rejection, repeated coverage thinning, flattening distance fade,
uncorrected authored-crown slope contact, nearly uniform upward normals and inconsistent
palettes. The procedural root repair does not cover authored crowns. Saved Low settings
compound sparse coverage; no preferences were changed. Historical captures/timings and
current source facts are distinguished from pending measurements. No repair, build/test
or new renderer benchmark was performed. N6 and grass performance acceptance remain open.

Update 20 October 2026: rebuilt (docs/WALKING_GROUND.md §18, TERRAIN_SCENERY_PLAN.md T4 and T5). The fixed whole-face
grids, the shader-only rejection, the compounded thinning, the shrinking far ring, the up-forced normals, the missing
specular and transmission, the separate palettes and the authored crowns' bilinear roots are gone: clumps of five species
of our own on worker-built tiles, one habitat rule, roots on the drawn triangles, real bent normals with a bounded glow,
the terrain's grass colour at the roots, the scanned plants as accents. At the T0 clearing the High GPU frame fell from
21.3–21.6 to 15.7–17.7 ms and the owner's profile from 9.3–9.8 to 4.7–5.0 ms. Still open: the owner's look review;
looking straight down shows the floor between clumps and from tens of metres up the vertical cards vanish; forest-floor
clumps read dark under the canopy's ambient occlusion; motion checked on frames 2 m apart only, not on a clip; the
user's Low settings not captured.

## Frontier AK recoil jumps and drifting optics — 17 October 2026

Owner report: iron sights vibrate/teleport, the 4× image seems disconnected from
the weapon, and the holosight dot misses screen centre. Reproduced live procedural
shot-reset discontinuities in hip/iron/holo/scope and an additional hip reach
correction jump. The first candidate overlaps fire recovery, uses continuous gun
reach correction, centres reticles on camera aim and registers the scope image
to that camera. Both bodies pass the native recoil/optical checks after lowering
the holo emitter below its glass opening. These checks do not establish visual acceptance:
the owner then rejected the support hand in the captures. The rifle profile now
approaches from below-left with its palm toward the fore-end and wider finger curl.
Side/underside captures were inspected, and both bodies pass the final burst checks
with that grip. Owner review is still pending. The station's near-target traverse
oscillation was also fixed by tracking from its pitch pivot rather than the moving
muzzle. The final build and all 60 focused checks pass. One earlier lossy test run
hit request deadlines; its single-test rerun and final grouped run passed without
transport-rule changes. These timings are recorded in the validation logs.
See [HELD_ITEMS.md](HELD_ITEMS.md) and [DEVELOPMENT.md](DEVELOPMENT.md).

## Defense battery interior sound is missing — 17 October 2026

The user's play log reports `No sound event 'defense.fire.interior'` when
StationsView.Shot requests the own-shot/same-hull sound through Sounds.Flat and
AudioDirector.Play2D. The defense generator emits report and mechanism events but
omits the interior recipe; the catalogue and its generated files are absent. This
warning does not establish an extra authoritative shot, memory leak or crash.

P5 has prepared a distinct four-variant interior recipe and a registry-wide battery
sound check in `.artifacts/p5-defense-interior-prep-r1`; neither is installed. Play2D
needs top-level files, so aliasing a layered report would still be silent. Generation,
focused tests, import, measured audio limits and own/same-hull/offship listening remain
pending. Existing source archives match their recorded hashes; that is not listening
acceptance. Preserved evidence: `.artifacts/recovery/user-play-log-20261017-1835.txt`
and `p5-defense-interior-preparation-root-review-20261017.json` beside it. See RECOVERY
for execution gates and P5 ownership.

**Installed 19 October 2026.** P5's recipe (`_iw_defense_interior` in `art/audio/events.py`)
was applied unchanged and built with the normal pipeline: four top-level files,
`iron-wake/defense_interior_01–04.ogg` (impact, -21.0 LUFS, true peaks -3.5 to -4.1 dBTP,
0.85–0.95 s), no new sources (the same CC0 firearm and Kenney recordings the defense report
and mechanism already use, and our own synthesis). `SoundCatalogueCoversBatteryFire` requires
report, mechanism and a non-layered interior for every battery in `batteries.json` (siege and
defense, the only batteries StationsView fires); it failed on the old catalogue and passes now.
`Sound` tests 8/8 and the in-engine `--sound-check` (332 events, 1771 files, 0 problems) pass.
Status: fixed in data; listening (own gunner, crew elsewhere aboard, the ripple overlapping the
siege gun) and the mix values (-3 dB, priority 84, six voices) await the owner's approval.

## Bow points left of the crosshair — 17 October 2026

The owner's screenshot reproduced in the FPS workshop: the drawn arrow points about 26°
left, while its large near-camera fletching fills the right side. The existing bow profile
adds 27° aiming yaw, -38° roll and 20 cm toward the camera after the forward aim placement.
Those values predate admire. Bow/arrow model axes are correct; the server launches along
the player's view independently of these visual offsets. Existing tests checked hand
attachment and release/ammo behavior, but never the rendered arrow's aim. Corrected the
profile and added a final bow-axis constraint with two-hand reach solving. The camera
clearance adjustment is FPS-only, preserving full-body shoulder placement. Final validation
passed: 220 native bow samples, 492 admire samples, 26 focused tests and an isolated hosted
real-input firing/redraw/admire tour. The nock remains at least 22 cm from the camera in
the raised-pose matrix; both bodies' final screenshots were inspected. Accepted images:
`docs/images/bow-alignment/final` and `bow-drawn.jpg` / `bow-after-admire.jpg` beside it.

## Held-item workshop shutdown warning — 17 October 2026

The admire validation runs and contact-sheet captures exit successfully but report one
ObjectDB instance leaked at exit. No resource/class attribution has been established;
this remains an editor cleanup warning, not a claim of a gameplay leak. Evidence:
`.artifacts/admire/native-rooted-{male,female}.err` and `rooted-*.err`.
The intermediate admire candidate also exposed forearm cut ends by moving the complete
FPS rig; that candidate was replaced with rooted arm solving and the final images are
in `docs/images/admire`. Both bodies pass 492 combined pose samples with stationary
shoulders, preserved grips and exact idle endpoints.

## Pilot cockpit steering leaked into third-person orbit — 17 October 2026

The owner reported unusable ship controls in TPS. The input route consumed RMB orbit
motion but allowed every other pilot mouse motion to reach the persistent cockpit stick;
the existing turn also survived V. Hosted gameplay now consumes all third-person mouse
motion in the orbit route, clears both cursor and smoothed steering on camera changes,
and excludes mouse steering when reading TPS flight input. Arrows still pitch/yaw, the
other flight keys still work, and throttle/assist survive switching. The TPS hint now
names arrow steering, RMB orbit and wheel zoom. See [REPAIR_SHIPS.md](REPAIR_SHIPS.md).

The full solution builds without warnings/errors and 33 focused checks pass. The
expanded hosted `repair-ships` tour asserts actual outgoing yaw/pitch for both cameras,
all mouse-button cases, arrow keys and releases. Its first run was stopped by the
memory guard during world loading, before reaching these assertions; it is not a
passed gameplay run. A bounded native Godot scene then passed 27 assertions against the
actual production input handlers, including both camera transitions, all mouse routes,
arrow steering/release and retained throttle/assist. Its production HUD captures were
visually checked. This fixture omits world simulation/network; the complete hosted tour
remains pending memory headroom. Evidence and limits are recorded in DEVELOPMENT.

## New owner playtest and merged repair queue — 16 October 2026

The owner supplied twelve further reports and eleven screenshots. Current tracking is in
[REPAIR_BACKLOG.md](REPAIR_BACKLOG.md); the numbered source and original images are in
[playtest-2026-10-16.md](reports/playtest-2026-10-16.md). This queue supersedes historical
planned/done labels below where newer evidence or owner rejection disagrees.

Cliff coverage/composition, terrain tiling, grass quality, pickaxe poses/reach, other tool
framing and oversized holo remain open. The owner now accepts the observed axe animation.
New requirements are readable ballistic flight, unattended chosen-angle hold with flight
assist ON, higher/clearer gunner views, roofs without side panels, and min/max gib control.
Overall performance must improve without hiding a quality downgrade. The dedicated node
pack exists and is installed; screenshot 3's entity still needs identification. These are
recorded requests and source findings, not repairs completed by this checklist update.

## Maximum world route: startup pass and remaining graphics issues — 17 October 2026

After the built transport-liveness, foliage temporary-allocation and terrain-array lifetime
repairs, one unchanged-quality Maximum/day in-process hosted route completed at normal
timeouts: six stops, 18 ready captures, 628.031 m, native exit 0. This closes the missing
follow-up evidence for that route; it does not establish normal child-server startup,
prolonged-play stability or a measured memory/FPS improvement. The earlier failures remain
retained. The 8 GiB world launch requirement remains in force.

The new baseline still shows parallel terrain texture bands, short/clipped-looking grass
rows, sparse/jagged foliage, strongly patterned foam, a stepped distant water edge and a
hard scalloped monument ground-patch border. Six stationary means are 24.93–30.20 ms,
with GPU means 24.36–29.63 ms. These observations do not isolate a cause. P2's staged visual
candidates had not been applied for that baseline; N3/N6/N12 and broader environment
acceptance stay open.

Read-only source review has now selected a two-file procedural-grass candidate addressing
root attachment, growth/bend scaling and the clipped blade silhouette, while retaining the
existing height multiplier, litter, density and quality settings. Root verified protected
inputs, reviewed the proposal and checked patch applicability. The exact two-file patch is
now applied. A duplicate-path preservation-map error was corrected; the first build was
then refused before launch at 5.0365 GiB available, below its 5.5 GiB threshold. After
headroom recovered, a fresh solution build passed with zero warnings/errors and the
Architecture/EnvironmentRoute checks passed 7/7. Root rehashed 9,760 protected inputs,
including the 896 built outputs. No native/import/render ran. Shader output, terrain
attachment, silhouettes, wind/trample behavior and GPU cost remain unverified.
The separate foliage proposal that removes alpha-to-coverage is excluded. Evidence is in
`.artifacts/p2-environment/candidate-review-r1/` and P2's report.

At shutdown the same run reports seven leaked Texture RIDs and two ObjectDB instances.
P8 reviewed this evidence separately from the 30-instance roof-viewer warning: this route's
capture method already disposes its Image, so the other viewer's Image-lifetime suspect
does not apply without further evidence. No verbose class attribution, leak fix or
FPS-causality claim exists yet. Five clock corrections
occurred, without route timeout or lost connection. Evidence and exact limits are in
[P2's report](repair-results/P2-environment.md), `docs/images/repairs/p2-environment/baseline-max-day-r4/`
and `.artifacts/p2-environment/baseline-max-day-r4/run/`.

## Roof backing slits and viewer shutdown warning — 17 October 2026

The first reviewed native metal-square LOD0 roof capture shows the removed gable open,
but also bright slits in the slopes. P7 traced a representative missing backing face to
`tools/repair_building_glbs.py`: triangles in the same rounded coplanar group choose
different projection axes from their unrounded normals, so incompatible 2D polygons
are subtracted. The raw face exists; both the preserved old and current repaired roof
lack it. The deterministic per-group basis correction passed five focused regression groups
(36 tiny repairs), reproducing the old defect and preserving the corrected backing while
retaining real overlap subtraction, winding and attributes. A scratch metal candidate now
restores the known backing and all 4,608 sampled surface points, preserving bounds, hulls
and metadata. The subsequent read-only diagnosis resolves the 93 sampled locations into
92 near-coincident pairs with positive projected intersections, none exactly coplanar in
stored coordinates. All have local raw-surface equivalents; the two additional square-LOD0
samples restore backing removed by the old repair. Three small triangles are exactly
zero-area and match old geometry and full vertex attributes. These findings support
controlled visual review without another geometry correction; they do not establish
exhaustive coverage or absence of z-fighting. Root verified all 6,347 protected inputs.
No further pruning or installation occurred. Installed roof packs remain unchanged; other-grade,
merge/fracture and native visual validation remain open. The apex
profile uses unchanged deck coordinates.
See [P7's evidence](repair-results/P7-building.md); N10 remains open.

The same capture saved successfully with ready=True and exit 0, then reported 30 ObjectDB
instances leaked at exit. An older disconnect run reports the same count, without proving
the objects or cause are identical. P8 identified static gib-world/callback retention and
captured-Image lifetime as candidates; neither explains the warning conclusively. A separate
verbose diagnostic is pending, with no production fix or rerun yet. See
[P8's evidence](repair-results/P8-performance.md). No roof-causality or FPS claim follows.

## AB Test B combat synchronization — implemented 16 October 2026

The [shared attack lifecycle](ATTACK_SYSTEM.md) replaces successful-shot identity and timeout
guessing with connection-scoped attempt IDs, authoritative results, resource reservations and
once-only presentation. Cannon acceptance retains its chamber reservation until matching state
arrives; barrel selection is replicated. Locally empty/cooldown cannon presses submit no delayed
fire request. Melee uses reliable identified starts and cancellation, with authoritative contact
timing and identity-based hit feedback. Rejected speculative projectiles are removed, and reliable
owner/rocket endpoints reconcile through loss, duplication, reordering and reconnects.

The original reproduction remains in [COMBAT_SYNC_AUDIT.md](COMBAT_SYNC_AUDIT.md). Current test,
render and performance evidence, including native test-run/startup failures, is recorded in
[DEVELOPMENT.md](DEVELOPMENT.md). Prediction remains provisional: authority can refuse stale,
invalid or expired attempts, and corrected effects cannot erase a muzzle flash already seen.
This repair does not claim a new physical melee collision model or unlimited packet-loss tolerance.

## AB Test B loading at 97% and reduced FPS — 16 October 2026

An isolated admin-camera gameplay check on 16 October also reproduced startup stalls exceeding
the existing ten-second transport timeout (three attempts disconnected before the tour began).
Extending only the isolated capture build's client/server timeout to 120 seconds allowed the
camera tour to finish. That run logged WebP decoding failures for imported Substation ivory/ORM
textures, Vulkan texture allocation failures and the renderer warm-up limit. These are unresolved
startup/resource findings; the camera feature does not change timeout, assets or loading policy.
Evidence: `.artifacts/admin-self-camera/tour*.log`; the capture caveat is recorded in DEVELOPMENT.

During the disconnect-menu check on 16 October, two direct `--host` starts lost their connection
while initial ground/character/plant assets loaded, before the menu action was tested. The server
reported `Timeout` and the client `PeerNotFound`; both transports use a ten-second timeout. The
engine-free suite was running concurrently, so this does not establish ordinary startup frequency.
This separate startup-liveness issue remains open; disconnect does not change transport timeouts.
Diagnostic logs: `.artifacts/disconnect-check/cold-start.log` and `second-direct-start.log`.

Earth terrain revision 2 reproduced the same timeout during two High-quality captures at its generated
inland landing site (in-process and child-server hosting), before dense nearby cover finished loading.
Those runs are rejected as visual evidence. Separate Low-quality child-server captures at the new route's
beach and from orbit both reached Playing/ready and saved screenshots. This does not resolve or establish
the exact cause of the inland loading stall. Logs: `.artifacts/earth-land-tuning/ground*.log`,
`beach*.log`, and `globe*.log`; accepted images and scope: [EARTH_TERRAIN.md](EARTH_TERRAIN.md).

The owner reports loading held at 97% and substantially reduced frame rate compared with the beginning of this
project (80–144 FPS, possibly on Low). The final `steady` loading stage formerly required 0.75 consecutive seconds
of frames below 33.4 ms with no maximum wait. All assets could be ready while a slower renderer prevented play
indefinitely. Its data-driven five-second limit now applies only after every preceding loading stage finishes;
regressions cover slow frames, unfinished dependencies, reset and normal smooth completion.

The owner's recent log also contains a null scene-tree error in gib warm-up. Loading now owns its gib preview nodes
instead of calling the deferred live gib singleton before insertion. This is a separate startup defect.

Rendering diagnosis compares the saved custom settings and Low independently. The saved custom settings enable
SDFGI and raise terrain/plant/grass/model detail; earlier High/Maximum route timings are not the owner's original
Low baseline. See [REPAIR_PERFORMANCE.md](REPAIR_PERFORMANCE.md) and [DEVELOPMENT.md](DEVELOPMENT.md) for current
measurements and validation. Neither a loading timeout fix nor reduced detail alone establishes restored FPS.

## AB Test B throttle at first boarding — 15 October 2026

The owner reports that W/S would not adjust throttle immediately after entering a ship until boost was used.
The old `ShipControls.Read` reset every throttle value inside ±0.02 to zero. With the default lever rate
of 1.25 per second, each frame's change stayed inside that range above 62.5 FPS, trapping the lever at neutral.
Boost could still request motion through its separate input; it does not unlock the lever.

The current working code already uses `PilotSteering.AdjustThrottle`, which stops at zero only when crossing
neutral and permits the next held frame to leave it. This report adds dedicated `ShipThrottleTests` for
both directions, 30–240 FPS, neutral crossings, retained position, limits and tiny changes accumulated before
network quantization. Build/test evidence and the live-validation limit are recorded in [DEVELOPMENT.md](DEVELOPMENT.md).

## AB Test B held items — reopened 16 October 2026

The owner reports poor first-person grip/composition, swing motion, and visible hit contact.
Source investigation confirmed that handle-held tools ignore catalogue first-person placement;
the item-only preview and gameplay disagree; fingers and swings are rebuilt procedurally;
the swing's cosmetic impact shake also plays on misses; and remote action timing does not
retain the item consumed/thrown from the final stack. The approved repair covers every held
item and includes a visual authoring editor. The 15 October technical implementation and assistant
review covered all 41 held-model profiles, 175 authored actions, separate FPS/TPS renderers,
tick-aligned contact and the workshop.
The rifle's real magazine now moves with the support hand; full-cycle captures checked the
reload camera-clipping repair. Those inspections were assistant acceptance, not owner acceptance.

Status: **six-tool curves rejected; Quaternius metal-hatchet pilot under review, 16 October 2026**. The owner says the workshop pass
did not produce good results and everything looks almost the same. The first requested priority is
**rock and gathering tools: carry, grip and swing**. Six rock/axe/pick profiles now have revised
carry, finger fits and swing curves. The assistant judged the new captures improved, but the owner
rejected the result and reports inability to drag/use the workshop to correct it. The UI has only
an opt-in FPS translation gizmo, without direct skeletal posing or rotation handles; the reported
drag failure itself has not been reproduced through real mouse input in this follow-up.
Read-only audit found standard Quaternius clips already imported but bypassed by the profile-driven
held actions. The owner requested a Quaternius trial and subsequently called the test much better,
but found the axe/hand too large. The metal-hatchet pilot now samples imported arm rotations in
FPS/TPS and attaches the item to the hand; its complete FPS rig is framed smaller/lower. Source
fidelity and strike timing are checked on both bodies. Carry is a frozen source pose and the
windup leaves the FPS frame; rock/other tools and the wider authoring workflow remain open.
The current loader also discards non-pelvis translation tracks,
so a first-person arms/prop pack cannot simply be dropped into the existing body-clip loader.
The workshop now exposes primary-palm gap, wrist bend and reach correction, plus an undoable
finger-fit command. Clean build, focused 27/27 and final male/female 24-action / 404-sample checks
pass; these are historical checks of the rejected curves. Legacy one-handed profiles still do not
consume free left-hand position/rotation channels in FPS; the skeletal pilot preserves the source's
free-arm motion. TPS grip is not covered by a measured guarantee.
See [HELD_ITEMS.md](HELD_ITEMS.md) for workshop instructions, historical tests, captures,
adaptive reach behaviour and the existing ObjectDB/Jolt shutdown warnings.

One place for everything that is known to be wrong or missing in the ground game, where it was found, what causes it,
which package fixes it, and whether that package has started. Before this file the list was spread over the owner's
playtest chats, the handoff, the design doc's "left for later" notes and the session memory, and the owner was not
aware of most of it. The rule from now on: **every playtest finding goes here with its cause and its package, the day
it is found; a fixed one gets its date and commit.** The handoff points here; AGENTS.md lists it among the docs to
read first.

Status words: *open* (nothing done), *planned* (in a package below, not started), *designing* (a design doc is being
written or waits for the owner), *building* (an approved package is being built), *waiting* (needs the owner's word),
*done* (with the date).

## 1. The second playtest (14 October 2026, about 3 am): 30 findings, none fixed yet

The owner played the whole build and wrote 30 findings. Six read-only agents traced each to its cause in the code the
same night, and a plan of three waves was proposed (§4). **The plan was never approved, so nothing of it was built.**
The same day went to the kernel (GROUND_SYSTEMS.md §12), the merges into `main`, the scale proposal (SCALE.md) and the
F2 developer panel (GROUND.md §8.5), none of which touch these findings. Numbers are the owner's.

### The world and the map (findings 1 and 3): now the scale proposal

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 1 | The map does not show the whole world; it is flat on a sphere. | The map covers a 3.9 km square, 6 % of the 16 km zone, and opens zoomed to 1.5 km; flat is right at zone scale (2 ppm distortion). | Scale, package S3: one zone holds the whole 2.4 km world, and one map shows it as a globe that becomes a flat chart as you zoom in (WHOLE_WORLD.md §11). The owner asked again on 14 October 2026, evening, said go for S3 and approved its design. S3d (15 October 2026): the G screen and the death screen draw the whole world, a globe from 4.8 km across that becomes a flat chart down to 150 m, with the grid of faces A to F, 25 × 25 cells a face ("C K12"), the same names in `whereami` and `teleport <cell>` (WHOLE_WORLD.md §14, S3d as built). | done (S3d, merged into `main` 15 October 2026) |
| 3 | 30 to 80 fps, thick forest everywhere, no biomes, valleys, rivers, lakes, cliffs or flat land; the 4K scans unused. | Everything that varies the land changes over 60 to 2,200 km, so a 16 km zone gets one landscape; no code for rivers, lakes, cliffs or plateaus; 8 of 34 scan sets reach the game, none of the 62 scanned cliffs and rocks. | World v3 was to be a design first; the owner's scale decisions of 14 October (small worlds, landmarks) replace it: SCALE.md carries the world's features at walking scale. Package S4: [WALKING_GROUND.md](WALKING_GROUND.md) (15 October 2026): landmasses from the real sea cover, relief provinces (plains, hills, tablelands with escarpments, mountains with snow), streams from source to the sea or a lake, lakes, canyons and gorges, sandy and rocky coasts with sea cliffs, woods in stands and clearings at 135 trees a hectare at most, the scans' plan. S4c (WALKING_GROUND.md §17): the game draws it, with sixteen 4K ground layers from the owned scans (all worlds), lakes' and streams' water with the sea's, and the woods at their densities; six ground-level shots. | designing (the design and its renders wait for the owner; the generator is built behind `--ground walking` on `main` since 15 October 2026; S4c, the game drawing it, built on the branch `s4`; S4b speed, S4d cliff meshes and S4e going live remain) |

### Performance and hit feedback (finding 23, plus the frame rate)

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 23 | Chopping a tree: the swing and the hit on the tree go out of step; the frame stutters. | Every axe hit rebuilds and re-uploads all the plants around the player on the main thread (20 to 30 ms a swing, and every 8 m walked); the chips and the sound wait for the server (70 to 150 ms after the visible contact, even hosting; GROUND.md H12: "predicting one's own hits is left for later"); the arms swing every 0.855 s and the server every 0.867 s, so they drift apart. | Performance and hit feedback: a budget for main-thread uploads; one's own hits predicted at the moment of contact and reconciled with the server; the swing clocks aligned. | planned (wave 1) |
| – | 30 to 80 fps in the forest. | The moon's light draws the forest's shadows a second time in daytime whenever the moon is up; the forest density itself (300 trees a hectare). | The same package for the moon; the density is the world's (SCALE.md). | planned (wave 1) |
| 19 | Hit and headshot sounds are hard to hear. | They are 3 dB quieter than the shot and arrive a server round trip late, buried under the gunshot. | Performance and hit feedback. | planned (wave 1) |
| 24 | The hit marker does not say what was hit. | The server knows exactly what was hit but sends only its kind. | Performance and hit feedback. | planned (wave 1) |

### Ships and keys (findings 28 and 29)

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 28 | Q and E open menus while flying; a seat should be left with Space. | The interface's keys do not know the player is seated: Q opens crafting (Q is also roll), any open screen zeroes every flight axis, so one press of Q and the ship cannot be flown; Space is the ship's up; only F leaves the seat, and only at rest; nothing on screen lists the keys. Three handlers hard-code keys and the pilot's keys exist in two copies. | Seats, controls and damage: keys get a state context (on foot, seated, in a menu); Space leaves any seat; the keys shown. | done (15 October, IW2, IRON_WAKE.md §5.7): the keys' context is one place (`SeatKeys.Context`: on foot, or seated as helm, passenger or station); seated, Q, E and 1–6 open nothing; Space leaves any seat; the ship's up and down moved to Shift and Ctrl (boost to B; F and H retired); the helm's keys on the flight HUD, another seat's above the belt; a seat's door, lock and descent keys come from its data. Test: the owner's playtest |
| 29 | Noclip is too slow. | Fixed at 10 m/s, 40 m/s with Shift. | Seats, controls and damage. | planned (wave 1) |

### Building (findings 2, 4 to 11, 14)

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 2 | Foundations cannot be placed on uneven ground. | The ground under a foundation may differ by only 0.91 m, because every foundation model is exactly 1 m deep (RustMVP's number). Needs a deeper footing model and the owner's choice of depth (3 m proposed). | Building. | planned (wave 1) |
| 4 | The first foundation lands left or right of where the player looks. | Its rotation snaps to a fixed 15° world grid, and near a building it jumps to any socket within 1.75 m, even an occupied one. | Building. | planned (wave 1) |
| 5 | A triangle floor fell when a half wall was destroyed, although two walls touched it. | A floor is held only by the piece whose socket it took; walls it touches do not count, though the contact is already detected. The owner's answer: a floor is held by every wall it rests against. | Building. | planned (wave 1) |
| 6, 7 | Floors and window walls glitch where they meet. | The stone floor, window and doorway meshes have faces drawn twice, on top of each other, since the kit was imported; every grade's window openings and the metal wall frame too. The import never checks a mesh for duplicate faces. | Building (the kit re-imported with a duplicate-face check). | planned (wave 1) |
| 8 | A door's opening direction cannot be seen while placing it. | The door's preview is drawn closed in one flat colour, with no handle, hinge or swing shown. | Building. | done in two steps: L8 (the leaves drawn part open) and D2 (20 October, DOORS.md §3: the hinge side and the swing side are chosen apart with R, and the ghost draws the arc the leaf sweeps); owner's look pending |
| 9 | A code lock cannot be placed on the middle garage door of three. | Most likely the lock's line-of-sight check treats the garage door's own 8 cm panel as an obstacle; "can't see it" and "something is in the way" share one message. | Building. | planned (wave 1) |
| 10, 14 | Objects need more room than they visibly take; the workbench fits but is refused; no snapping to walls. | Deployables' collision boxes do not match their models (the workbench's is 32 cm wider); touching counts as overlapping; nothing squares an object to a wall. | Building (sizes compared with the art at import). | planned (wave 1) |
| 11 | A placed object flickers for a moment. | The see-through preview stays over the real object for half a second and the two fight for the same pixels. | Building. | planned (wave 1) |

### Interaction and bodies (findings 12, 13, 15, 25, 27)

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 12 | E should name a bed or bag; hold E a radial with Pick up and Give to friend. | Beds and bags only have Pick up, not set as the tap action, so a tap does nothing; naming does not exist anywhere (no name on deployables, on the wire or in the save). | Interaction and bodies. | planned (wave 1) |
| 13 | E on the vehicle workbench does nothing. | The same dead tap: its only verb is Pick up; the Tin Finch is crafted from the normal crafting menu near the bench. | Interaction and bodies. | planned (wave 1) |
| 15 | Bodies end up in the wrong place, fall through floors, cannot be harvested. | The ragdoll most likely starts at the zone's origin before the body is moved into place (unverified); ragdolls collide only with the ground; the body becomes a bag only on the player's own screen. | Interaction and bodies: bodies lie where the player died, are looted and harvested like a carcass, then become a bigger bag. | planned (wave 1) |
| 25 | Radial titles are cut off. | The radial shrinks titles into a fixed width and never wraps them; the cost and reason lines are not fitted at all. | Interaction and bodies. | planned (wave 1) |
| 27 | A ship's fuel tank cannot be opened. | Whatever is opened on a ship is always its cargo; four places assume that. | Interaction and bodies: a one-slot tank opened from the fuel cap. | planned (wave 1) |

### Weapons and gear (findings 16 to 22, 26)

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 16 | The pistol's support hand is bent wrong in first person. | The support pose points the fingers down while the forearm comes up from below, a wrist bend of about 115°; first-person poses are hard-coded per kind and ignore the catalogue's poses. | Weapons and gear (the pose), then P7. | planned (wave 1) |
| 17 | Attachments are not what they should be. | The zoom and the stat changes work; sights never use their own eye point, there is no reticle or scope overlay, the weapon light is never drawn; attaching by dropping and a remove button were an earlier session's shortcut. | Weapons and gear: attachment slots in the gun's panel, one sight, one muzzle, one underbarrel, dragged in and out. | planned (wave 1) |
| 18 | The bow does not draw. | Its draw and release animations exist but never run, because only guns set "aiming". | Weapons and gear. | planned (wave 1) |
| 20 | Armour values are not shown. | Protection per body region exists in the data; the inventory does not show it. | Weapons and gear: a protection panel with the damage types. | planned (wave 1) |
| 21 | Recoil does not feel like Rust. | The numbers are right, but the view kicks and returns by itself (an earlier shortcut); the gun barely moves, no camera shake, no first-person sound layer, crack or casings; sensitivity does not scale with zoom. The owner's direction: old Rust, 2017 to June 2022, learnable patterns, the view moves and stays. | Gunplay feel: a design first (wave 2), then the work. | designing (not started) |
| 22 | Character animation and grips look wrong. | Third-person grips do not fit the hands to each item; first-person poses are code, not data; no proper bow hold. | Characters and animation (P7): a design first (wave 2). | designing (not started) |
| 26 | Rockets: the HV rocket must kill any living thing; the regular one should fly slower and drop more. | Drop is set on the launcher, so both rockets fall alike; the HV rocket does less to living things (175) than the regular one (210), and a bear has 400 health. | Built with IW4 (the incendiary rocket's package touches the same data): a round's own gravity in weapons.json (the rocket 0.3, the HV rocket 0.1 of the planet's: 10.6 and 0.7 m over 50 m), and the HV rocket's 600 to whoever it strikes, so a bear and a player in the best armour die (GROUND.md §6.6; `FireTests`). Rust's raid counts unchanged. | done (15 October 2026, `iw4`, 5aad578) |

### The architecture (finding 30)

The owner suspected the shared systems were being bypassed. The trace agreed about the edges: harm that is not an
attack (falls, hunger, a wrecked ship) calls `Hurt` and `Die` directly and skips the damage pipeline, so a crash kills
through god mode; keys have no state context; main-thread uploads have no budget; model sizes are never compared with
gameplay sizes; some data is read by nothing (the weapon light, sights' eye points, casing sounds). Each wave 1 package
fixes its bugs and adds a guard so the problem cannot come back: only the damage pipeline may change health, enforced
by the compiler, with one test that god mode survives every source of harm. Status: planned (wave 1); the keys' state
context is done (IW2, finding 28).

## 2. Gaps the design doc admits

These are written in GROUND.md as decisions of the form "left for later" and were never in a plan.

| Where | What is missing | Status |
|---|---|---|
| GROUND.md 76, H12 | One's own hit plays a round trip after the swing; predicting one's own hits is left for later. The same as finding 23. | planned (wave 1) |
| GROUND.md H14 | Outcrops are still spheres to walk into and for the swing's thick ray, taller than their broken stages; only the weak spot uses the drawn surface. | open |
| GROUND.md 52 | A broken tool vanishes; Rust keeps broken items for repair. | open |
| GROUND.md §4.9 | No burning sound (nothing in the protocol says a player burns), no sand footsteps. | open |
| GROUND.md 75, 78 | Sleepers stay ragdolls; others' aiming pose, crawling and sleeping poses are not drawn, because the character contract has no flag or stance for them. | P7 (wave 2) |
| GROUND.md §5.11 | Replication is 2 to 3 ms of the tick on one thread; writing each player's snapshot on worker threads is the next step if a zone needs 200 players. The risks of doing it are listed in the handoff of 14 October. | open (not needed for one server yet) |
| handoff | Night bloom on some glowing models; two Jolt crashes under low memory (unconfirmed); the 40 to 60 ms ragdoll hitch; stand-in models (`standIn` flags in `models.json`); sound mixing by ear; cheap-looking effects; Linux server testing. | open |
| handoff | The wardrobe hookup: looks for every remote player and the inventory preview; sleep and crawl stances; aiming for bows; the help-up, eat, bandage and inject events. | P7 (wave 2) |

### Found by reading the code (14 October 2026, the surveys for S3)

| What | Cause | Status |
|---|---|---|
| A herd's members start on one spot, face one way and think on the same tick. | Every animal added on the same tick gets the same seed: `AnimalWorld.AddMember` hashes the counter `_localIds`, which is never incremented. | fixed (S3a; on `main` since the merge of 15 October) |
| Near tree impostors most likely disappear more than about 400 m from the landing site whenever the landing site is out of view (not yet seen in a run). | In play the near set is anchored at the frame's origin (`Vegetation.Game.RebuildNear`), and the near impostors' `CustomAabb` is a fixed ±400 m around that anchor, so Godot culls them by the wrong box. | done (S3c, 15 October): the near set is anchored at the camera |
| The map of a surface without a sea is most likely black (not run). | `ZoneMapImage` takes sea level as −∞ when `SeaRadius` is 0, so heights become infinite and the hill shading NaN; only the `--stand-in` world has no sea. | done: since S3a the stand-in world has a sea, and since S3d the map reads `SeaRadius` safely (a world without one is all land); `MapTests.MapPictureIsTheWholeWorld` draws one (WHOLE_WORLD.md §4, §12) |

### Found by the load test on the whole world (15 October 2026, S3b)

S3b ran every load test with 100 bots on the ball, at the landing site and on beaches all round it (GROUND.md §5.12),
and fixed on `whole-world` what broke there. Still open:

| What | Cause | Status |
|---|---|---|
| A moving character costs about half as much again in a snapshot as on the flat patch: 152 kbit/s down with 99 walkers in view (114 before S3), and the 1,200-byte budget holds about 87 of them a snapshot. | A position on a ball changes in all three coordinates, and the view's two octahedral halves both change as it turns (WHOLE_WORLD.md §8). | open: positions coded along the ground |
| When 80 bases start at once on beaches all round the world, 30 of 3,602 ticks go over 33 ms in receiving (the 99th percentile 27 ms). | A placement's checks against ground the tick hasn't touched yet took 3–30 ms each; a foundation's no longer asks about the ground (S3b). | open |
| Animals cost 2.0–2.7 µs each a tick, 2.1 on the flat patch. | Every look at the nav grid finds its face coordinates by two arctangents; on the plane it was a division. | open, small |
| The load test's bots share one copy of the building, which keeps one viewer's flags for a door (the last bot's that heard of it). | `BuildingReplica` keys door states by door only. The bots use their own doors by the verb they know (S3b). | open, bots only |

### Found while building IW1 (15 October 2026, ship spaces on `iw-spaces`)

| What | Cause | Status |
|---|---|---|
| In the ground game on the stand-in world (no surface view), every frame throws in `NatureView.Hook`, so the rest of the frame's drawing (people, things, the aim, building, raids, ships) never runs. | `GroundView` sets `NatureView.World` only when it opens a surface view; on the stand-in world `World` is null and `Hook` reads `World.Plants`. | done (S3c, 15 October): the surface view draws the stand-in world, its trees, berry bushes and ferns are the zone's natural objects (`NaturePlants`), outcrops and pickups `NatureView`'s models |
| Drawn people faced up to 60° away from where they looked on the ball. | `CharacterFrame`'s north is the flat world's −Z laid on the ground; the movement code's is the world's pole. | done for live people (IW1: `CharacterFrame.YawToward` in `PeopleView`); corpses, sleeping bags and other things drawn by a yaw keep the old convention |
| The ground game's sky has its horizon tilted on the ball. | The stand-in's procedural sky is the world's +Y up, not the landing site's. | done (S3c, 15 October): the game draws the planet's own sky over the ball, its ground test and horizon the ball's (WHOLE_WORLD.md §14) |
| An admin's ship spawn found no room for the Iron Wake near the landing site (58 of 60 spots had a tree in its footprint), and on a slope it rested on its hull with a velocity the contacts never let it move by, so it never parked. | The spawn's footprint test refuses any tree; the motor slept only on three loaded skids at under 5 cm/s. | done (IW1: trees and rocks in the way go for an admin's spawn; a ship held still with its engines off sleeps) |

### Found while building S3c (15 October 2026, the client draws the ball, branch `s3c`)

| What | Cause | Status |
|---|---|---|
| The stand-in's temperate forest draws a closed forest floor with ferns, though its trees stand far apart (about 24 a hectare). | The drawn ground takes its tree cover from the biome table (`BiomeTable.Cover`); the stand-in's trees come from gather.json's flat densities, which the surface doesn't know. | open, until S4's ground |
| From the leaving height the sky past the ball's edge is the sky mirrored about the air's horizon, and clouds stay hidden behind the ball. | The air keeps the real planet's size (WHOLE_WORLD.md §10.2: the minimum); clouds are drawn by the sky, behind all geometry. | S5 |
| On the real-size look-dev view, ground textures jump on lines every 4 km of a cube face, and the grass grows on 0.6 m cells (sparser than before). | Texture coordinates keep floats exact by a per-tile anchor at multiples of 4,096 m; the grass's cells are the cube faces' at the level nearest 0.46 m (WHOLE_WORLD.md §14). | open, look-dev only |
| A screenshot taken right after a fast load (shaders cached) showed translucent orange and white puffs and streaks in the air round the player; 50 s after the start they were gone. | Not traced: something emitted in the first seconds of play (perhaps the effects' warm-up, or a start-up effect drawn at full size); not seen in the slower first loads. Check on `main` too. | open |
| A full test run crashed natively in Jolt (`JPH_MeshShapeSettings_Create2`, access violation) during `SurvivalNetTests.OthersSeeOnlyABag`, while three Godot games ran beside it. | Not traced; the code it ran is unchanged by S3c, and the next full run, alone, passed (239). Perhaps the Jolt crash under low memory the handoff lists. | open |

### Found in play (15 October 2026)

| What the owner saw | Cause | Status |
|---|---|---|
| Hosting on `main` crashed while starting the world: Windows' error box for `Hollow.Server.exe` (exception 0xe0434352). | Both checkouts host with the same save (`user://hollow-host.db`). A `whole-world` build had upgraded it to schema 8; `main`'s server knows schema 6, threw, and nothing caught it. Leaving the world then threw again in `TerrainLod._ExitTree` (`SemaphoreSlim.Release(0)` when no worker had started). | done: each branch hosts with its own save, a server says why it refuses a newer save and stops cleanly (the menu shows it), the terrain's exit releases only started workers (GROUND.md §4.3); the upgraded save was moved to `hollow-host-whole-world.db` |
| The Iron Wake's hull is open at the bow and the stern, with see-through gaps all over it (the owner's screenshots, from outside). | Two causes. Back faces Godot culls (a self-crossing window, single-skin drive jackets, LOD4's opened shell): repaired by IW-A (IRON_WAKE.md §9.1 "As built: the holes"). And openings in the asset itself, which no back face shows: the space under Deck A open at both ends (round the keel lance under the bow, under the ramp's hinge), 0.5 m slots beside the bridge open from the sky to the hold, a slit at the bow's top, the side panels' 4-5 cm seams showing the linings, cracks across the bow section; 1,613 of 563,316 rays from outside met the hull's inside. | done (15 October, `iw-close`, IRON_WAKE.md §9.1 "As built: the bow and the stern closed"): the import closes them with pieces of its own (an armoured embrasure round the keel lance, a bulkhead under the ramp's hinge clear of its swing, tread plates beside the bridge, a backing behind the seams) at every level, and fails when rays from outside meet the inside (`inside_seen`): 12 rays at LOD0. Two collision pieces where a player could have fallen through. Commit c1971f9. |
| The barrels look hollow behind: "as if the bullet is going to teleport; the barrel should be whole". The defense mounts' tubes end at a breech face with an open bay behind them, and a siege round crosses an open gap into its breech. | The loading mechanisms (IW-A part 2) brought the breech out of the closed receivers into open space. | building (`iw-art2`): whole barrels into closed breeches that open to take the round |
| The F1-kill finding: flying a Tin Finch, the owner killed himself with the F1 console's `kill` and respawned; at the Iron Wake the prompt read "[E] LAND FIRST: YOU CAN ONLY STEP OUT OF A SHIP AT REST", as if the game still had him seated. | Nothing of the seat was left: the server frees a dead pilot's seat on the ship's next step, and the client stops flying when its snapshots stop naming a ship. The Iron Wake beside him never came to rest: on uneven ground its hull, pushed out of a bump beside its pads, moved a few millimetres every tick while going nowhere, and the motor slept a pilotless ship only under 1 mm a tick with a pad loaded (13 of 49 spots round the landing site; a ship afloat or on its side never slept either). A client knew a ship at rest only by its landing or its sleep, where the server also counts one barely moving near the ground, so every verb at it was refused with the pilot's "land first". | done (15 October, IW2): a ship with its engines off that touches anything (pads, hull, the sea) and barely moves rests and sleeps; the server's one rule of a ship at rest goes to clients (`ShipNetFlags.Still`, protocol 25 after the merge with IW4's and S4's); on foot the refusal says the ship is moving (`ShipMoving`). Every way out of a seat now goes through one release (a death at once, an admin's teleport, a lost connection, a disabled station, a wreck; IRON_WAKE.md §5.7). Commit 9da9bdd. Tests: `SeatTests.AShipLeftAloneComesToRest` (49 of 49 park; a Tin Finch on its side too), `SeatTests.KilledInFlightAndRespawned` |
| Aiming at the Iron Wake's defense mount offers "BOARD IRON WAKE". | Every part of a ship offers the Tin Finch's whole-ship verbs; ships have one seat and four fixed parts. | done (15 October, IW2, IRON_WAKE.md §5.7): every ship's parts are its data's, each with its own capabilities and name; the mount offers MAN FORWARD STARBOARD MOUNT. Test: `ShipPartsTests.EachPartOffersItsOwnVerbs` |
| The guns' dark steel reads as blotchy, square-patched rust up close. | Every steel finish derives from Poly Haven's Rusty Metal Sheet (the asset's material revision). | building (`iw-art2`: worn gunmetal and paint with rust at edges, seams and cavities) |

## 3. Deferred structure and decisions waiting on the owner

| What | Why it waits | Status |
|---|---|---|
| P6, one model for every world object | Each kind of object still has its own storage, network sync and save code; the biggest remaining piece, to plan with the owner. | wave 3, designing |
| P7, characters and held items | Grips, clipping, Rust's closed grip, first-person poses as data, full-body clips from local AI tools. | wave 2, designing |
| The rename from claim device to tool cupboard in the code | Deferred because another session worked in that code at the time. | open, small |
| The zone host, transfers between zones, the lifecycle with catch-up (GROUND_SYSTEMS.md K14) | Deferred by the owner on 14 October: one dedicated server for now. | deferred |
| SCALE.md | The scale proposal (2.4 km worlds, a galaxy of landmarks) decides findings 1 and 3. On 14 October 2026 the owner said go for S3 (one zone for a whole world, the globe map) and approved its design, WHOLE_WORLD.md; it was built on the branch `whole-world`, S3a (the zone is the whole world) and S3b (the load tests on the ball, GROUND.md §5.12), and merged into `main` on 15 October 2026 at the owner's word. S4 (the ground at walking scale) is designed in WALKING_GROUND.md with look-dev renders, and built behind an off-by-default switch on the branch `s4`, for the owner's review. | S3 building (S3a and S3b on `main`; S3c and S3d next); S4 designing; the rest waiting |
| `prototypes/map-ux` | Three web prototypes of the galaxy map wait for the owner's choice. | waiting |
| The front door | The menu as the default start, a Developer page listing the viewers, a desktop shortcut (asked on 14 October after the F2 panel). | waiting |
| The owner's window size | Asked in the playtest plan and never answered; the frame-rate work depends on it. | waiting |

## 4. The plan (proposed 14 October 2026, 3:49 am; awaiting the owner's go)

**Wave 1, five packages in parallel, each on its own branch, each checked once (build, tests, one screenshot), merged
when green, then one playtest.**

| Package | Findings | Size |
|---|---|---|
| Seats, controls and damage | 28, 29, 30; keys with a state context; Space leaves any seat; only the damage pipeline changes health | medium |
| Building | 2, 4 to 11, 14; the kit re-imported with a duplicate-face check; sizes compared with the art | medium to large |
| Interaction and bodies | 12, 13, 15, 25, 27; naming; bodies that lie where the player died | large |
| Performance and hit feedback | 23, the frame rate, 19, 24; an upload budget; one's own hits predicted | medium to large |
| Weapons and gear | 16, 17, 18, 20, 26; attachment slots; the protection panel | large |

**Wave 2, designs first, each for the owner to approve:** the world at walking scale (now SCALE.md); gunplay feel after
a study of Rust 2017 to June 2022 (finding 21); characters and animation (P7: 16, 22 and the wardrobe hookup).

**Wave 3:** P6, the shared object model, alongside the world work; then monuments in the game and more worlds.

**Defaults proposed with the plan, not yet taken:** Space leaves any seat, the ship's up and down move to Shift and
Ctrl; a footing about 3 m deep (Rust's number to be checked); a floor is held by every wall it rests against; attachment
slots in the gun's panel; a one-slot fuel tank opened from the fuel cap; recoil as in old Rust; bodies lie where the
player died, are harvested, then become a bigger bag; a protection panel with Rust's damage types; plants stay made in
code and the scans go into ground, cliffs, rocks and deadwood after a licence check.

## 5. Done since the list was made

- IW6 (15 October 2026, branch `iw6`): seated people are drawn sitting at their seats, every ship's, the Tin Finch's pilot
  included, hands on their grips (GROUND.md §11 87; IRON_WAKE.md §9.6). The effects library's weak spots: a loop's
  intensity, trails at speed laid along the path, loops scaled whole (fires of other radii), a moving shooter's velocity,
  and flame cards dim at their feet (`fire.patch`'s white domes) (GROUND.md §9.3). Still open: the soft-puff smoke batch;
  the helm's own light switch (IRON_WAKE.md §9.4); unverified in play, every one (no renders in this package).

- The first playtest's findings (13 October: looking, holding, free foundations) were fixed in `ground-look` and
  merged (GROUND.md §11's decisions for `ground-look`).
- The 30 merged worktrees under `Hollow-wt` were removed (14 October).
- The kernel (GROUND_SYSTEMS.md §12), the F2 developer panel (GROUND.md §8.5) and the merge into `main` (14 October):
  architecture and tools, not fixes to the findings above.

## AB Test B repair validation findings — 15 October 2026

Current implementation/evidence is tracked in [REPAIR_STATUS.md](REPAIR_STATUS.md); historical proposals above do
not override the current design or the owner's authorized repair work.

- Vegetation requests overlaid their full-scatter flag on a packed cube-face bit, so distant tree requests could
  suppress later nearby vegetation permanently. Separate address/mode keys fix this; two regressions and the
  previously stuck actual teleport pass.
- Ordinary natural hits rebuilt nearby plants and outcrop tiles without any rendered presence/stage change.
  Visible-state invalidation removes those redundant uploads while retaining cues, weak spots, depletion and fall.
  Four regressions and actual hit-event traces pass. First-hit setup and broader stutter remain open; concurrent
  external GPU work prevented a clean overall FPS comparison.
- GroundTerrain evicted a still-requested corner tile beyond its radial cache radius, preventing the building
  viewer from settling. Requested tiles are now retained; the 45-view joint tour completes with all 73 images.
  Changed pixels under camera movement are diagnostic data, not a blanket proof that all mesh flicker is resolved.
- A fitted scope passed geometric alignment checks but presented a tiny view through its physical tube. It now
  images the shared world into the rear lens only during ADS. Day/night captures, hand/eye alignment, culling and
  render shutdown pass. Night validation uses the actual player's planet position and rendered sun elevation.
- God-mode immunity, ordinary death/respawn and the complete corpse-to-backpack sequence pass their recorded tests.
  The original reported native crash still has no supplied stack trace; known engine exit resource warnings are
  retained in the evidence, not represented as a clean shutdown result.

## Normal-ground contact resonance — 16 October 2026

The owner reported a strong "boink" on ordinary walking and jumps. Removing generic heavy-impact layers alone
failed the owner's first listening comparison. Grass/dirt now use different short recorded contacts and single
landings. The owner confirmed both source samples lack the defect, then confirmed it is also absent in the actual
`gameplay-grass-mix.wav` recording. Two hosted movement/cadence captures and final file measurements pass.
This closes the reported boink for that grass gameplay sample; dirt runtime, other materials and the wider sound
mix remain open. [Implementation and listening evidence](REPAIR_AUDIO.md); [full action audit](reports/audio-action-checklist.md).

## Settings footer text overlaps APPLY — 16 October 2026

Fixed and visually verified in the three specified window/scale cases. At native 1600x900,
changing a graphics setting previously showed a long orange NOT APPLIED sentence
that extends underneath the APPLY button, obscuring its ending about Escape discarding changes.
Reproduced during N11's actual production-UI acceptance check in
`.artifacts/p8-gibs/settings-acceptance-runs/apply-r2/02-thirty-unapplied.png` (also captures 01
and 04). `GroundUi.Settings.cs` draws that sentence from design x=506 without a width limit;
footer buttons begin at x=934. Saved-state text fits. APPLY/cancel/persistence passed, so this
is a layout defect rather than a settings-save failure. The admin/UI owner implemented a
footer-only candidate with compact complete APPLY/Escape guidance and a measured GPU-text
column ending 16 design pixels before the unchanged buttons. Shared and isolated capture-host
builds pass. After the earlier resource preflight hold, all three native captures completed:
1600x900 scale 1, 1280x720 scale 0.8125 and 1920x1080 scale 1.25. Root inspected all nine
dirty/saved/after-cancel images: the complete status and GPU line remain clear of the four
buttons. Actual APPLY and Escape input paths passed in each process (116/116 combined
assertions), with unchanged real preferences and empty stderr. Receipts and images:
`.artifacts/admin-settings-footer/runs/{automatic900-r1,automatic720-r1,scaled1080-r1}/`.
This closes the footer layout defect; broader physical-input and in-world settings acceptance
are separate. See RECOVERY for resource measurements and checkpoint preservation.

## Environment foundation validation and remaining gates — 18 October 2026

Biome weights previously exceeded one when ice and alpine overlapped. Corrected the
remaining-land partition and explicitly initialized offshore relief fields. Independent
world/sample tests pass; generated population compatibility advances Earth to save revision
5, other walking bodies to 2 and protocol to 37 without changing landform seeds/heights.
Previous saves are preserved and refused. Terrain/grass job retirement and physics disposal
now pass targeted native/lifetime checks. WORLD_ENVIRONMENT_ARCHITECTURE.md records the
implemented foundation and the unimplemented bounded-store/scenery work.

Full acceptance remains open: the broad suite did not complete under available memory;
large-furnace fire-effect catalogue and station-firing checks reported failures. A standalone
BatteryTests process also reached the memory guard. Hosted capture attempts produced no
usable screenshot (first outer timeout, then memory-guarded retries); they establish no
visual or GPU performance claim. The earlier Jolt native mesh-shape abort is still unassigned,
even though disposal now drains workers after managed failures. Since 19 October both ground paths
refuse a malformed tile before Jolt sees it (`GroundMeshCheck`: no triangles, a non-finite vertex,
an index out of range, or only degenerate triangles) with `MalformedGroundTileException`, which names
the tile; if the native abort returns, its tile was none of these. To catch it, run the suite with
`DOTNET_DbgEnableMiniDump=1`, `DOTNET_DbgMiniDumpType=4` and `DOTNET_DbgMiniDumpName=<folder>\hollow-%p.dmp`
so the .NET runtime writes a full dump when the process dies (not yet tried on this crash). See DEVELOPMENT.md's
Environment architecture and T0 checkpoint for commands, evidence paths and exact limits.

Follow-up: hosted High capture r13 now produced an inspected 1600×900 baseline after
replacing a timed-out WMI memory query with direct Windows memory counters, with unchanged
guard limits. The view confirms the long straight cliff wall, disconnected rock dressing
and uneven grass coverage are still open defects. The new closed module is not active.
The capture also logged an unrelated missing metal-blade import and exit resource warnings;
it is useful visual evidence, not clean acceptance or a performance measurement. The item
was subsequently imported. See `.artifacts/environment-ownership/hosted-high-r13/`.
