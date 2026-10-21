# Current owner repair request — 15 October 2026

These are the current requested outcomes. The two legacy text reports are historical evidence;
their old plans, defaults, approval requests and cleanup instructions are not current commands.
Screenshot: `reported-shore.png` in this directory.

1. With flight assist on, leaving the pilot seat preserves flight at the throttle/movement the pilot left. Continue while fuel remains, until engine off through hold-E radial menu.
2. Planet generation is visibly poor: absent/broken grass, only one grass type, empty terrain, missing resource nodes and monuments. Find supplied fuel, sulphur, metal and HQ metal resource-node assets. Improve variety and environment quality; this is not accepted final art.
3. Iron Wake turns much too slowly; make it substantially more agile, especially rotation.
4. Mouse ship control flickers and the small control area is awkward. Moving/dragging mouse right should keep turning right. Desired reference: Battlestar Galactica Online controls.
5. Any seated player (pilot, passenger, turret) can toggle a free third-person orbit camera centred on the player. Wheel zooms; hold left mouse and drag changes viewing angle. Separate camera input from controls/firing.
6. Fix plastic terrain. Aim for grounded, immersive environment quality informed by Rust, Sons of the Forest, DayZ and No Man's Sky, using the good available textures.
7. Rotate the actual 3D inventory character with left-mouse drag.
8. Repair unnatural first-person arm/hand weapon poses and broken character animations, especially guns/pistols.
9. Find and use a proper coherent sound library; fix wrong footsteps and audit sounds for every action without placeholder shortcuts.
10. Implement drowning/breathing and an underwater screen transition; improve currently poor water rendering.
11. Dress cliffs/hills with actual rock/cliff assets rather than texture alone. Inspect `D:\GameDev\Projects\RustMVP` and its recreated Rust map as reference.
12. Admin noclip mouse wheel adjusts speed. Extend F1 console with useful Rust-like command binds rather than only hardcoded controls.
13. Audit and address all 30 legacy findings in `legacy-30-issues.txt`; do not assume the old proposal or existing status labels prove correctness.
14. Fix god-mode players dying (including ship crashes) and the client crash associated with death while god mode is enabled.
15. Holding H for hover-loot remains active while inventory/storage is open even when pointer leaves its UI panel; release only when appropriate (key up/UI closed).
16. Fix planet-relative animal movement, collider placement and orientation, AI; research Rust behavior. Animals must not attack a player standing on top of a foundation they cannot reach.
17. Provide exclusive fullscreen and borderless fullscreen choices, plus windowed mode.

Owner authorizes parallel development and asks us to decide sequencing; work is substantial across sessions.
No artificial engine precision changes or realistic astronomical simulation are requested.
