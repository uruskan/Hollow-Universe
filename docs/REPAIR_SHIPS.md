# Ship handling and seated cameras — 15 October 2026

Current requests R1, R3, R4 and R5 are recorded in `reports/current-17-issues.md`. The current design and these requests override inherited Elite-style control descriptions.

Leaving the helm now retains throttle, lateral/climb commands and flight-assist mode. Turning and held boost are released. The ship keeps simulating and consuming fuel with an empty helm. Its crew can hold E while looking at the ship or a part aboard and choose **Engine off**; this disables thrust/assist without deleting momentum. **Engine on** restarts it if fuel remains. Sitting does not override an explicit off switch. Shared interaction reach, sight and authorization gates apply, and this is the existing ship capability rather than a second interaction path. Grounding and collisions still affect flight. This is local flight, not autonomous obstacle avoidance or interplanetary navigation.

Ship saves are version 4, retaining translational commands/assist and the explicit engine switch. Versions 1–3 remain readable; they have no retained command. The pilot snapshot now includes the retained movement command so taking the helm does not reset its throttle. The explicit engine-off flag is read by both authority and prediction.

Iron Wake tuning is in `game/data/ground/ships.json`: pitch/yaw/roll rates are now 28/32/30 degrees per second, with acceleration 75/90/85 degrees per second squared. Previously these were 9/8/7 and 6/5/5. Assisted pitch/roll limits are 35/25 degrees with a 1.4 return coefficient. These are responsive carrier tuning values for playtesting, not realistic naval inertia.

Mouse steering is a persistent cursor with an 80 ms response constant. Moving right leaves a right-turn request until moved back; it no longer decays or discards small inputs through a dead zone. The visible cursor travels across a 220-interface-pixel reach instead of the old 70-pixel ring. MMB explicitly centres steering. W/S adjust the retained throttle; X sets throttle to zero. A/D strafe, Shift/Ctrl rise/sink, Q/E roll, B boosts, Z toggles assist. A related high-frame-rate bug is fixed: small throttle increments no longer snap back to neutral every frame.

The reference is the developer-authored BSGO **Developer Blog 8 — Strike Controls**, dated 30 August 2013, preserved in an [archival reproduction](https://bsgo.fandom.com/wiki/Dev_Blog_8). It describes cursor displacement controlling pitch/yaw and separates steering engagement from looking. The original official page was not available in the search results. This implementation adapts that directional behavior; it does not claim an exact reproduction of the old client or its optional dead-zone setting.

Every ship seat, including passenger and turret seats, has **V** to switch between first-person and a player-centred third-person orbit. In third person, hold LMB and drag to orbit; wheel changes distance from 1.5 to 120 metres. The local seated character uses the same socket, clothing and grips as remote players. The camera follows the player's position, not the ship centre, and its heading is independent of subsequent ship turns. Its eye remains above terrain. Zoom can pass through ship geometry for inspection; collision-aware interior camera occlusion remains a presentation limitation.

Third-person mouse gestures never change turret aim/fire or steering. A pilot's existing steering request and keyboard flight controls continue while orbiting. Turret shooting/zoom operate in first person; changing camera modes while LMB is held cannot fire until the button has been released. Q/E retain their flight roles; the engine radial is used after leaving the seat. The standalone `--view ship` development sandbox retains its legacy ship-centred chase camera; these general seated controls belong to hosted/dedicated gameplay.

Space also leaves small aircraft in flight, using a clear authored exit beside the hull and inheriting the craft's velocity at that point. It never teleports the player down to terrain. This releases the seat and allows normal gravity/fall consequences; a fully blocked exit still refuses.

Validation: the coordinated production build succeeded and focused tests passed, including all seven `ShipRepairTests` and three `AdminInputTests`. Ship cases cover actual empty-helm flight, small-craft airborne exit/momentum, fuel exhaustion/engine switch/reseating, persistent saves, frame-rate-independent steering and throttle, a bounded 90-degree carrier turn, engine-off prediction parity and camera orbit geometry at arbitrary planetary up. UI gesture behavior still requires the coordinated live client check, beyond the engine-free tests.

# Noclip speed and console bindings (R12)

The F1 console now accepts `noclipspeed 40`, `bind F6 "noclip"`, `bind MWHEELUP "noclipspeed 80"`,
`unbind F6`, and `binds`. `bind F6` shows that key's command. Bindings accept physical letters, digits,
function keys, common named keys, wheel directions, and optional `CTRL+`, `ALT+`, `SHIFT+` chords. They
run once per press through the ordinary console; server commands retain their existing admin checks.
Bindings and the preferred speed persist in this game's local user settings. The F1 console and open
menus keep their own keys; seated controls run before shortcuts, including the orbit camera's wheel.
These are console-command shortcuts, not a replacement for held movement actions or command scripts.

While the server has granted noclip, wheel up/down multiplies/divides the preferred speed by 1.25 unless
that wheel direction has a binding. Speed is 1–200 m/s in integer wire units (default 10); Shift multiplies
by four. Both prediction and the server use that same quantized input. A speed request cannot enable
noclip and has no effect on ordinary walking. The compact world's existing flight bounds still apply.
`AdminInputTests` covers speed changes, malformed values, sprint, exact prediction and ordinary-player
input. The binding test covers modifier normalization, wheel aliases, validation and server permissions.

Command naming follows the primary [Rust keybind documentation](https://wiki.facepunch.com/rust/Keybinds)
and [Facepunch's bind-to-noclip example](https://rust.facepunch.com/news/devblog-93); implementation is local.

## Battery stabilization with the faster hull

The subsequent full-suite run exposed 3.191 degrees of battery tracking error against the unchanged 0.5-degree
requirement. The siege mount's old 40 degrees/s² acceleration could not follow the hull's new 90 degrees/s².
Its yaw motor is now 45 degrees/s with 180 degrees/s² acceleration; pitch is 40/120. The defense mount was already
faster. Mechanical arcs and the firing alignment threshold remain unchanged.

The shared `StationRules.Slew` also used a target one tick beyond the ship pose supplied to it, even though both
server and client call it with the current tick's pose. It now arrives at the current target; target velocity still
feeds the bounded motor controller. Full-circle target velocity wraps across the angle seam, and an empty seat
clears its target history. A new engine-free case exercises full 32 degrees/s hull yaw, 90 degrees/s² starts and
reversals, and the existing mechanical limit. Root coordinates compilation and test results.


## One-slot ship fuel (legacy L27, 15 October 2026)

The fuel cap now says **Open** and opens a single normal inventory slot. Holding E on the ship also offers **Open fuel tank** to its crew. The cap retains the historical internal `refuel` verb ID to preserve stable interaction indices; it no longer transfers anything automatically. Cargo and battery magazines keep their existing routes. Fuel access follows the usual interaction reach/sight gates and remembers the part that was actually opened, including the hull, so opening from the far end of the Iron Wake does not immediately close against the distant cap.

The slot takes only the ship definition's fuel (lamp oil), with the authored capacities of 500 for the Tin Finch and 6,000 for the Iron Wake. The shared container rules now support an accepted item and destination stack limit. Dragging or quick-moving out of the large tank produces ordinary 500-unit carried stacks; excess stays in the source. Wrong-item swaps and overfill are refused. The same constraints accompany full and incremental container messages, including an empty slot, and are used during optimistic client replay. The existing loot panel labels this store **FUEL TANK**.

Whole unspent units live only in the tank. When an engine opens one, its unburned fraction lives in a separate reserve and cannot be withdrawn as a fresh item. That reserve occupies capacity until burned, survives closing the inventory and restarting, and remains available when the whole-unit slot is empty. `ShipState.Fuel` is the motor/HUD/prediction total derived from these two values, not another store. A motor event reports the exact burn so tiny burns remain effective even below the resolution of a full 6,000-unit float. The final fraction powers only its corresponding fraction of a tick and clears engine/boost flags on exhaustion. Explicit engine-off still consumes nothing. Wreck salvage keeps the inherited half-fuel rule, now splitting it into valid carried stacks and clearing the tank once.

ShipCodec 5 appends unopened-unit count and reserve. Versions 1–4 migrate their old fuel float exactly once. New saves ignore the earlier cached float when the appended authority is present. Restore clamps malformed counts and fractional values. The narrow shared inventory extension also applies to attachment moves through their existing `Accepts` checks.

Added six `ShipFuelTests`: both tank capacities and ordinary stack withdrawal, wrong-item/full-tank refusals, fractional withdrawal/redeposit/full burn and tiny burns, v5 persistence and v4 migration, actual wire round trips plus optimistic container replay, cap/hull access and reach, and the final powered fraction followed by an empty tick. Updated the existing boarding/refuelling scenario to use an explicit inventory move and existing ship save/repair fixtures to load authoritative fuel. The coordinated fuel/battery/building/network run passed 68/68 (`.artifacts/repair-focused5.log`), including the unchanged strict ship prediction checks. Full-suite and fuel-panel screenshot validation remain pending the root's coordinated run. The partial unit is shown in the ship fuel total rather than as a draggable fractional item.

Prediction note: the own-ship snapshot additionally carries `FuelReserve`. The motor and authoritative tank share the same whole-unit/fraction burn arithmetic; comparing only rounded total fuel would otherwise create needless corrections each snapshot. Existing strict ShipNetTests remain unchanged and must pass.

Final build18 ship run: `.artifacts/repair-ships18.log` passed the full actual fuel/boarding/cockpit/V/LMB/wheel/W/
airborne-exit/unattended-motion sequence. Current cockpit/orbit hints were visually inspected. Four untouched
captures are retained in `docs/images/abtest-b/repairs/ships`. Passenger/turret runtime camera validation remains
separate from this pilot sequence; shared rule/input tests do not substitute for those captures.
