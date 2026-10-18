# Hollow Universe AB Test B

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
station and hidden-camera contexts suppress the ground marker; physical optics and dedicated ship sights remain
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

## Quality gates and remaining development

The inherited game is a foundation, not a finished implementation of this design. Compilation,
engine-free tests, multiplayer checks and actual rendered captures establish each milestone.
The first connected milestone targets gathering, crafting, building, ship boarding, flight,
two-player interaction, and saving/rejoining. Interplanetary travel must be tested as real continuous
gameplay before being reported complete.

Further work packages include compressed system layout and travel; server handover between worlds;
planetary streaming; surface/space visual transitions; modular ship upgrades; ship capture;
planet-size-dependent monument placement and loot; weather; shared oxygen equipment; electricity; turret authorization;
autominers; research/rare variants; respawn points across worlds; fauna; sound and impact feedback;
save recovery; multiplayer security; load testing; and visual polish. Proposed solo activities in
PLAY-03 remain proposals until selected. Each package must integrate with shared systems and be
validated before adding its full content catalogue.

## Coordinate decision

Compressed travel distances do not require a realistic astronomical simulation. Existing physics
uses planet-local and ship-local positions. Keep ordinary Godot and preserve that separation.
Double-precision world metadata can remain plain C# data without changing the Godot engine build.
The choice of coordinate frames is an implementation detail, not an extra player-facing travel mode.

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
