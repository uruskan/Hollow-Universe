# Hollow Universe AB Test B

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
