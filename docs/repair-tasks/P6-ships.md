# P6 Ship attitude hold and gunner visibility

Implement N9 and complete remaining R5/L28 live checks while preserving R1/R3/R4/L27/L29.
Read COMMON.md, REPAIR_BACKLOG.md, REPAIR_SHIPS.md and current ship ownership before editing.

Exact requested behavior: with flight assist ON, tilt Iron Wake, reduce thrust to zero,
leave helm, and keep that chosen angle while operating its guns. Current auto-level is
the unwanted behavior. Do not turn assist off or repair the ship toward level. Define the
held orientation's planet/local frame deliberately and test it; preserve intentional manual
control on return and existing engine/fuel/collision consequences.

Ownership candidates: ShipMotor, vacant-helm state/assist data and prediction, ship model
camera sockets, client ship-eye transforms and seated camera presentation. Reserve shared
GroundView, ship data and any net/save schema through coordinator before edits. Active P5
owns station firing identity, ammunition and projectile synchronization; do not duplicate it.

Suggested agents: simulation/prediction/hold-state semantics; per-station camera sockets and
seating sightlines; host/guest scenarios and controls regression with distinct files.

Acceptance: host/guest agree on tilted pitch/roll during zero-thrust unattended operation;
gun use does not reset orientation; returning to helm responds normally. Exercise engine
off/on, fuel exhaustion, grounding/collision, different radial locations and restart if new
state persists. Keep existing nonzero-throttle unattended flight working.

Inspect each gun station at aim limits, zoom, both cameras and different hull attitudes.
Correct low/blocked viewpoint while keeping believable seating, avoiding hull/near-plane
clipping and retaining RMB orbit/LMB fire isolation. Include passenger live orbit/zoom check,
pilot steering/throttle, Space exit, Q/E roles and one-slot fuel inventory regression.
Capture actual two-client gameplay and request runtime slot. Write
`docs/repair-results/P6-ships.md`.
