# Audio and visual-effects architecture audit — 18 October 2026

This report preserves the findings before the owner authorized implementation. The
resulting architecture is documented in [AUDIO_VFX.md](../AUDIO_VFX.md); subsequent
validation and remaining limits are recorded in [DEVELOPMENT.md](../DEVELOPMENT.md).

The project has substantial shared playback and rendering infrastructure. Its weakest
boundary is the translation from a gameplay contact into the material-specific sound,
particles and decal. That boundary still relies on client-side guesses and item-name
exceptions. It already causes incorrect responses for current content. Preserve the
directors and consolidate that boundary before expanding individual impact fixes.

This is an implementation audit of the current AB Test B working tree against the
shared-system rules and `docs/DESIGN.md`. Findings below distinguish source-established
behavior from measured behavior. No gameplay implementation or assets were changed.
The suggested work is a proposal, not an already implemented or approved design revision.

| Concern | Current assessment |
| --- | --- |
| Simulation and attack identity | Strong shared foundation: registered damage receivers, engine-free rules, predicted/authoritative attack reconciliation and bounded terminal history. |
| Footsteps | Comparatively cohesive: shared cadence, contact environment, equipment profiles and local/remote routing, with meaningful movement tests. |
| Audio playback | Shared pooled service with limits, priorities, variants and spatial processing; continuous-loop admission and recovery need work. |
| VFX rendering | Shared GPU batches, pooled transient resources, culling and LOD; continuous-loop budgets are incomplete. |
| Material response selection | Fragmented; authoritative collision information is discarded and reconstructed approximately in presentation. |
| Cue authoring and coverage | Partly data-driven; duplicated reload sequencing and missing/unused bindings remain. |
| Current performance acceptance | Not established by this audit. Build/tests verify useful contracts but do not measure crowded-scene audio/VFX cost or listening quality. |

Existing architecture worth preserving:

- [DamageSystem](../../src/Hollow.Simulation/Systems/DamageSystem.cs), around line 65,
  routes traces/damage through registered receivers. The client attack lifecycle reconciles
  launches and terminal messages; `ThingsView` consumes an endpoint once. Separate local
  predicted and remote authoritative entry points are appropriate.
- [AudioDirector](../../game/scripts/Audio/AudioDirector.cs), lines 36 and 529, has 56
  positional and 20 flat pooled players, event limits, priorities, cooldowns, shuffled
  variants, distance layers/delay and occlusion. Creating another player system would
  duplicate an existing concern.
- [FootstepDriver](../../game/scripts/Audio/FootstepDriver.cs), line 37, uses shared
  engine-free cadence and footwear data. Local and remote equipment resolve through
  the same profile registry. Replacing recordings through the established build pipeline
  is ordinary content work; it does not inherently add architectural debt.
- [FxDirector](../../game/scripts/Fx/FxDirector.cs) uses shared GPU batches and pools
  for flashes, lights, tracers and decals. Destruction uses a generic gib system with
  bounded simulation, sleeping, lifetimes and distance fallbacks. Gathering already
  associates sound and effect cues in data, providing a useful migration example.
- The offline audio pipeline retains source provenance and hashes, applies common
  encoding/measurement rules and supports incremental rebuilds. File/import/loudness
  checks are valuable safeguards to retain.

The material/contact defects are the highest-priority consolidation work:

1. **A confirmed hit loses information before presentation.** `TraceHit` retains target
   identity/part but lacks surface identity and hit normal. The server sends only its
   target family through [ProjectileEnd](../../src/Hollow.Net/Protocol/Messages.cs),
   line 609; its `Id` is the projectile ID, not the struck target ID. The endpoint includes
   position and attack identity but no target identity/part, material or normal.
   [GunEffects.SurfaceOf](../../game/scripts/Ground/GunEffects.cs), line 133, then searches
   nearby objects or raycasts the client's current building representation to guess again.
   Destroyed, moved, large or unreplicated targets can be classified incorrectly. Scanning
   replicated things and allocating a nearby-natural list per hit adds avoidable work,
   although this audit did not measure it as a bottleneck.

2. **The user's wood-versus-stone example has a concrete failure path.** Ordinary bullet
   impacts on stone compound walls/gates, large furnaces and metal deployables such as
   auto-turrets can select wood responses. These objects are missing from the hard-coded
   deployable list in `GunEffects.cs`, lines 148–166, whose fallback is wood. Deployables
   use `TargetFamily.Thing` through `WorldObject.Ref` and `ThingSystem.DeployableReceiver`;
   they do not take the ordinary building-piece grade path. The lookup chooses the nearest
   eligible object's origin within 2.5 m, so a neighbor can also supply a different wrong
   material. Incendiary ammunition replaces the ordinary visual effect but still uses
   the selected impact sound. Rockets have a separate presentation path.

3. **Animal impacts fall through to terrain classification.** `SurfaceOf` recognizes
   `Character` as flesh but omits `Animal`. The arrow impact sound also checks only
   `Character` in [ThingsView](../../game/scripts/Ground/ThingsView.cs), around line 441.
   These are source-established routing defects, not newly reproduced gameplay captures.

4. **Impact orientation is approximate.** `GunEffects.cs`, line 135, uses the opposite
   flight direction for object-hit normals and planet up for terrain. This cannot reliably
   orient grazing wall hits or hits on sloped ground. Compound geometry already computes
   a normal, but the deployable receiver uses an overload that discards it. Decals in
   [FxDecals](../../game/scripts/Fx/FxDecals.cs), line 95, retain a world transform rather
   than a struck-target attachment, so moving-hull marks cannot follow the hull correctly.

5. **Overlapping material rules live in several places.** `FootSurface`, `FxSurface`,
   grade maps, deployable-name switches, door-name substring checks and gib responses
   encode related classifications independently. `ThingsView.cs`, line 392, separately
   switches VFX surfaces into impact sound keys. Ship movement frames are uniformly metal
   in [FootstepEnvironment](../../src/Hollow.Client/Audio/FootstepEnvironment.cs), line 16.
   Distinct footstep, bullet and destruction responses are useful; their common physical
   surface identity should come from one declared source.

The resource budgets need correctness work before claiming scalability:

- **Audio loops:** `AudioDirector.Loop`, line 216, allocates voices for distant loops and
  layers starting at -80 dB. Allocation, line 529, uses priority/age rather than audibility;
  an existing same-event loop can prevent admission of another instance. Ship engine
  layers are requested on the running transition in
  [ShipSounds](../../game/scripts/Ships/ShipSounds.cs), lines 130 and 194, without recovery
  when rejected or stolen. Fire loops have a similar one-time request. Under saturation,
  a loop can remain absent until another lifecycle transition. This is a code-supported
  failure condition; no native saturation reproduction was run here.
- **VFX loops:** `FxDirector` declares `FrameBudget = 1500`, but its one-shot emission
  check at line 613 admits an entire layer after checking the previous total, allowing
  overshoot. Loop emission at line 853 does not check the budget. `StartNow`, line 706,
  bypasses per-effect admission and creates per-loop flame/light nodes when requested by
  the definition. GPU batch capacity remains bounded; total loop count and all loop CPU
  work are not bounded by that particle budget.
- **Loading growth:** catalogue-wide preload and stream retention exist. The inspected
  catalogue had 331 events and 1,210 distinct encoded sound paths (about 25.1 MB); tests
  count 1,767 file references because multiple events share files. This is not proof of
  a current memory problem. Introduce resource banks or lazy recipe groups when measured
  loading/memory needs justify them, after the correctness issues above.

Other integration debt is narrower and can be fixed within these systems:

- Local reload cues in [FirstPersonView](../../game/scripts/Ground/FirstPersonView.cs),
  line 293, and remote cues in [PeopleView](../../game/scripts/Ground/PeopleView.cs),
  line 703, separately schedule magazine-in at 55% and rack at 85%. The local path uses
  `HeldActionTiming`; the remote caller passes firearm reload duration. Shared action cue
  timing would prevent drift while retaining appropriate local/positional adapters.
- Shell and repair roles exist in the sound catalogue, but this audit found no gameplay
  consumer of those roles; the existing audio action checklist records the same gaps.
  A valid file or catalogue entry is not evidence that the intended action produces it.
- Blood duplicate suppression tracks one global last position/time in `GunEffects`,
  line 120. Existing attack identity would provide a more precise basis for suppression
  when several victims are hit close together.

Recommended implementation order:

1. Capture an engine-free contact result through the existing collision/target registry:
   declared physical surface, target identity/part, contact position/normal and reference
   frame. Preserve sufficient authoritative endpoint information for presentation without
   transmitting sound filenames or renderer objects. Reuse existing attack identity.
2. Resolve action + surface + applicable modifiers into sound, effect and decal responses
   in data. Declare surfaces on target/collider parts and terrain/grades. Migrate ordinary
   bullet/arrow contacts first, including compound walls, metal deployables and animals;
   then migrate overlapping melee, gathering and footstep classifications as appropriate.
   Keep the audio, VFX and gib directors responsible for their separate rendering concerns.
3. Separate persistent logical audio loops from allocated voices so inaudible loops can
   release voices and audible loops can reacquire them. Apply audibility-aware priorities.
   Enforce remaining particle capacity for every emitter path and explicit loop/light caps.
4. Share action cue timelines between local and remote presentation, then close missing
   item bindings and genuinely unconsumed action roles.
5. Add material-response and event-delivery tests, then bounded native stress scenarios.
   Only after that acceptance should broad per-asset polish resume.

Acceptance should cover stone building pieces and compound walls; wood/metal deployables;
animal/player flesh; disappearance of the target before endpoint delivery; oblique normals;
moving reference frames; owner/observer agreement; and duplicate/reordered delivery producing
one intended response. Native scenarios should assert voice recovery, bounded loop/particle
counts, cleanup and stable resource use across repeated enter/leave cycles, and measure
CPU/GPU frame cost under a stated population. Listening and visual review remain separate
from catalogue correctness.

Fresh validation from this audit:

- `dotnet build HollowUniverse.sln --no-restore -nologo -v minimal`: succeeded with
  **0 warnings and 0 errors**, 23.67 s.
- Selected engine-free checks: **44/46 passed in 24.6 s**. Filters were
  `SoundCatalogueTests EffectsCatalogueTests AudioRepairTests FootwearAudioTests
  DryFireAudioTests AttackProjectileTests AttackLifecycleTests AttackMeleeTests`.
- `EffectsCatalogueTests.EveryItemNamesItsEffects` failed for the missing `large-furnace`
  fire binding. `SoundCatalogueTests.SoundCatalogueCoversItems` failed for missing item
  sound mappings on the large furnace, compound structures, defenses and window fittings,
  among other current deployables. This does not imply every operational sound on those
  objects is absent; it is failure of the required item-catalogue contract.
- Footstep movement/contact, footwear equip parity/extensibility, dry-fire gating, attack
  lifecycle and projectile reconciliation checks passed. The material-routing defects
  above are outside those existing test contracts.
- Logs: `.artifacts/audio-vfx-audit-build-20261018.log` and
  `.artifacts/audio-vfx-audit-tests-20261018.log`. No fresh Godot capture, listening test,
  crowded-scene profile or full-suite run was performed. Historical source-project effect
  benchmarks are not acceptance evidence for today's integrated working tree.
