# P8 Performance gibs and reliability

Implement N11 and investigate N12, L3/L23, startup/cleanup/validation debt under COMMON.md.
Read REPAIR_BACKLOG.md, REPAIR_PERFORMANCE.md, REPAIR_LOADING_SHADOWS.md, ENVIRONMENT_ROUTE.md
and newest KNOWN_PROBLEMS/DEVELOPMENT records. The owner requires optimization while retaining
visual quality. Do not presume technical debt or a specific subsystem is the measured cause.

Ownership: profiling/measurement harness, gib runtime/catalogue/preference integration and
new tests. Reserve GraphicsSettings/SettingRows, shared loading/streaming/UI files explicitly.
Other packages own their renderer/simulation optimizations; deliver measured hotspots and
coordinate patches rather than editing their files concurrently. The disconnect and Earth
terrain chats are active. Preserve accepted shadow-budget/loading fixes.

Suggested agents: reproducible CPU/GPU/loading/stall profiles; saved minimum/maximum gib
control and bounded runtime behavior; cleanup/test reliability and regression scenarios.
All profiling/import/capture/full-suite runs still require one shared execution slot.

Current gibs have catalogue caps/distances/lifetimes but no dedicated user setting. Add clear
minimum/maximum control with saved preferences, predictable live changes and conservative
bounds. Define whether the UI controls amount/detail/budget explicitly. Cosmetic reductions
must not affect authoritative destruction, loot, collision rules or necessary combat cues.
Coordinate roof fracture asset regeneration with P7.

Baseline normal play at fixed resolution/quality/pose/time, then walking, first/repeated tree
hits, dense forest/cliffs, firefight and destruction. Record mean/median/p95/p99, CPU/GPU,
draw calls/geometry/instances, allocations and memory/streaming where relevant. Current Low
185 FPS versus Custom ~61 FPS is a preset difference, not a quality-preserving speedup.
The new environment also has measured woodland regressions. Attribute costs before changes.

Investigate remaining exit resources and loading-time Timeout/PeerNotFound disconnects;
these are distinct from the fixed 97% steady-frame wait. The original god-mode native death
crash needs reproduction/log evidence, while immunity rules are already repaired. Resolve
full-suite reliability with the active attack owner; don't patch its melee timing fixture in
parallel. Complete current live map coverage/Vehicle Workbench checks as bounded regressions.

Acceptance: matched before/after images and timings, no hidden quality downgrades, controlled
destruction at both gib limits, persistence/live settings, clean build and appropriate suites;
final integrated full suite and actual host/guest gameplay scheduled with coordinator.
Write `docs/repair-results/P8-performance.md`, including failed experiments and limits.
