# P5 Combat lifecycle and feedback

This package has an existing active owner: “Investigate weapon hit sync”, thread
`01a14453-042b-7b42-b6de-60c543f8f849`. Read COMMON.md, REPAIR_BACKLOG.md,
COMBAT_SYNC_AUDIT.md and current ATTACK_SYSTEM.md/diffs. Do not create a competing lifecycle
implementation or assume its unvalidated code has closed the audit findings.

Complete C1/C2/M1/M2/P1/P2, N8 projectile visibility, R9/L19/L21 audio/feel, L23 contact sync
and L26 live rocket verification. Preserve authoritative ammunition/damage and prompt local
feedback. False cosmetic shots and orphan projectiles must be eliminated by shared identity/
reconciliation, not sound cooldowns or hiding consequences.

Lifecycle owner controls AttackLedger/AttackModule, LocalGun/LocalMelee/StationModule,
simulation attack events, protocol generation/server modules and dependent FirstPersonView,
ThingsView/FxDirector/FxTracers changes. P4 owns visual grip/pose/projection and optics.
Additional audio/visibility workers need explicit file splits from this lead. Suggested agents
under that ownership: lifecycle/network permutations; ballistics/tracer/impact readability;
sound sources/routing/listening and recoil/feedback review. Do not duplicate existing agents.

Real finite-speed bullets, gravity and drag already exist. Tracers currently show every third
bullet including incendiary. Improve readable flight/drop and impact presentation at bounded
cost, tied to the accepted/rejected/corrected shot. Do not force always-visible meshes for
every projectile. Verify muzzle/camera parallax and endpoint agreement under varied range,
light, own/remote view and latency. Distinguish visibility from ballistic balance.

Audio carries forward dirt/other materials/transitions, remote listeners, animal recordings,
shell/repair consumers, hit/headshot prominence and broader combat masking. The grass boink
repair is accepted for its reviewed recording; preserve it. Gunplay/recoil feel remains open.
Record actual gameplay; source-file loading/measurement is insufficient listening acceptance.

Acceptance: two cannon chambers yield exactly two authoritative and two accepted visual shots;
correct initial barrel, rapid/short taps, refusal then success, state/confirmation ordering,
loss/jitter/reorder/duplicate/end-before-launch, switching stations/tools and cancellation.
Verify melee starts and real contact feedback agree under delayed inputs; rejected/ended
rockets retire once. Host/guest recordings count ammo, launches, visual events and damage
separately. Verify regular/HV rocket flight/drop and organic damage. Record CPU/network/FX cost.
Coordinate all shared execution. Write `docs/repair-results/P5-combat-audio.md`.
