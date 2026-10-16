# Shared execution contract

**After the 16 October computer restart, read [RECOVERY.md](RECOVERY.md) first.** Its temporary
limits override the earlier parallel-agent plan: lead-only recovery and one global heavy job.

Work only in `D:/GameDev/Projects/HollowUniverseABTestB`, branch `abtest-b`, under AGENTS.md
and current docs/DESIGN.md. The original Hollow Universe checkout and source asset library
are read-only references. Preserve the ground UI, C#, engine-free deterministic simulation,
shared data-driven systems, save isolation and authoritative network behavior.

Read REPAIR_BACKLOG.md, the package prompt, relevant implementation docs, current git status
and recent development log before editing. Active source edits may supersede this snapshot.
The owner authorizes fixes; the package lead decides routine implementation choices. Do not
restart working systems just because an old report says they were absent.

Assign up to three subagents disjoint paths before edits. The lead owns integration and review.
The listed paths are initial exclusive ownership within this campaign, not permission to
overwrite another active author's edits. If a needed file is currently owned/edited elsewhere,
prepare a narrow proposed patch or handoff and obtain an ownership transfer through the
coordinator; continue independent work. Do not reset, stash, delete or stage others' changes.

Shared catalogues (models/gather/surfaces), protocol and generated messages, GraphicsSettings,
GroundView, FirstPersonView, NatureView, Scatter, shared shaders, DESIGN/ARCHITECTURE and root
status docs have one editor at a time. Change shared interfaces deliberately; no parallel
protocol generation. Keep authoritative collision and presentation consistent.

The coordinator is chat `01a14455-2b1c-7e62-be75-bc958fc1779a`, “Review remaining checklist items”.
Use the existing user-authorized coordination route. Report file claims, dependencies and
requests for runtime validation there. If messaging is unavailable, write a package handoff
and keep working within the assigned independent scope.

Before a shared build, full test suite, Godot import/run or Blender/GPU capture, request the
coordinator's execution slot. Do not kill the owner's game or another chat's processes.
Source work and light read-only investigation may run in parallel. Isolated build outputs
require explicit isolation of generated/intermediate paths too; a copied binary directory
alone does not isolate builds. No competing GPU jobs during measurements.

Use isolated saves. Record actual commit/source hashes, camera/world position, seed/time,
graphics settings, resolution, readiness and scenario in evidence. Compare identical settings
for optimization claims; report mean/median/p95/p99 and CPU/GPU/draw/instance costs where relevant.
Reducing render scale, cover density or quality is a tradeoff, not an equivalent-quality win.
Asset-only previews supplement actual hosted gameplay, not replace it.

Run the solution build and appropriate behavior/network/persistence tests for implementation
changes. The coordinator schedules the final integrated full suite. Visual changes require
inspected original screenshots and action changes require full-cycle recordings. Test misses,
refusals, disconnects and conflicting inputs as applicable. Report failures honestly; an isolated
rerun does not turn a failed full-suite checkpoint into a clean one. Do not weaken assertions
or move gameplay reach/damage to make a presentation check pass.

Write results in your own `docs/repair-results/Pn-*.md`, linking retained logs/media and listing
changed files, validation, owner-review status and remaining work. The coordinator updates
REPAIR_BACKLOG.md/REPAIR_STATUS.md/KNOWN_PROBLEMS.md/DEVELOPMENT.md, preventing concurrent
rewrites. Commit only your owned files when validated, with exact scope; coordinate Git staging
and commits as well as builds. Recheck ownership before touching files outside your package.
