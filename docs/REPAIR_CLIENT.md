# Client repair package — 15 October 2026

Current scope follows `reports/current-17-issues.md`, the owner's design, and the historical report as evidence.
This package changes character presentation and inventory/settings interactions, preserving the existing ground HUD layout.

## Implemented

- **R7:** left-drag in the inventory character area rotates the existing 3D figure. Its rotation survives held-item and clothing rebuilds. Release, closing the screen, or losing window focus stops the gesture. Item dragging remains separate.
- **R15:** H is captured as a physical key at the root input stage. Hover-loot remains active across panel boundaries and child controls; root mouse motion keeps the target position current. Leaving a panel means there is no target, not that the held gesture ends. Key release, closing the inventory, and window focus loss clear pending transfers. The existing 350 ms transfer delay remains.
- **R17:** settings now have a DISPLAY tab with Windowed, Borderless, and Exclusive choices. These call Godot's distinct `Windowed`, desktop `Fullscreen`, and `ExclusiveFullscreen` modes. APPLY saves the choice, REVERT/BACK restores saved settings; quality presets preserve it. Returning to windowed restores the previous window size and position. Headless and editor-embedded runs skip changing the native window.
- **R8 / L16:** firearms now consume their existing `held.firstPerson` catalogue poses. Each gun also has an explicit `held.supportHand` frame and camera-space elbow pole. The pistol's support hand no longer uses the class-wide fingers-down frame that produced a severe wrist fold; rifle and launcher frames remain independently tunable. This does not replace the whole character animation library.
- **L18:** the bow held control supplies aim state to the first-person view, with ammunition, equip-time, and swimming checks. Draw duration comes from the item's actual bow data. Accepted arrow launches emit a client event and trigger the bow's string release and release sound; refused/unfinished draws do not fake a shot. Aiming is replicated for bows and consumed by remote character views (previously omitted for guns too). On release, the drawing hand remains at its former drawn position before following through rather than jumping to the relaxed string.
- **R10 support:** the existing status stack displays OXYGEN with the server's breath percentage and a red state when suffocating. The simulation, wire fields, and underwater rendering belong to the root/environment packages.
- **L20:** the inventory shows body projectile protection, body melee protection, and clothing warmth beside the figure. Hover explains exact head/body/limb values. These call the simulation's protection and insulation functions, so the panel does not invent an averaged armour value or unsupported damage types.
- **L25:** radial centre titles, descriptions, costs, and refusal/warning strings wrap by measured font width. Each field has its own region; long words can break instead of extending beyond the field.

## Files and checks

The changes are in `game/scripts/UI`, `game/scripts/Settings`, the character pose code and model catalogue,
`FirstPersonView`, `HeldControls`, and the existing client projectile and character-state paths. No new message schema is required for bow aim or release.

- `BowPresentationTests.DrawAndAcceptedReleaseReachTheClients` checks an actual loopback server with an archer and observer: drawing reaches the observer, drawing alone produces no release, and one accepted arrow produces one local release event.
- `ModelCatalogueTests` validates support hand axes and finite elbow poles alongside existing grip checks.
- `--view ui --ui-check` drives real input events for inventory figure rotation, mouse release, H outside panels, returning to loot while held, key release, and closing the screen.
- Existing character captures support `--view character --fp --item pistol|rifle|bow --ads --shot <png>`, and `--event fire --event-at <seconds>` can inspect bow release. The `--vm-orbit yaw,pitch,distance` option exposes the arms from the side for wrist review.

Coordinated production builds passed with zero warnings. The loopback bow test passed, including exactly one accepted
release and one consumed arrow; actual Godot `--ui-check` passed all 32 checks, including the new drag/H gestures.
Native window-mode checks also passed on Windows/Vulkan: borderless reported `Fullscreen` at 2560×1440,
exclusive reported `ExclusiveFullscreen` at 2560×1440, and windowed restored 1600×900
(`.artifacts/repair-window-check.log`). Character captures remain separately checked. First engine captures exposed a
pistol support hand sitting too high and a bow drawing hand obscuring the arrow despite better geometric wrist
angles. The support grip was lowered, and the bow nock moved onto the authored arrow-rest height. A concrete rendering
bug was also fixed: dynamically generated bow strings used a material override that did not receive the viewmodel's
projection/depth conversion. They now use the same projection as the bow and hands.

The character lab now schedules capture/event timing on the same capped animation clock as the viewmodel, so startup
shader stalls cannot spend the whole screenshot wait while only a few animation frames run. Every first-person
capture reports actual equip/aim/draw/release state. Full-draw, release, ADS and both body types still need rendered
review; a static wrist-angle calculation alone is not visual acceptance.

For the display tab capture use `--view ui --screen settings-display --shot <png>` after the next build.
`--view ui --window-check` checks the three native OS modes and restores the previous window without saving settings.

## Remaining work

**L17 attachment slots** passed the coordinated build, all four transaction/network/persistence tests, and the eight
attachment cases within the 45/45 actual-input UI checks. The selected
weapon shows its supported sight, muzzle and rail sockets, from its actual attachment list. A compatible part highlights
its socket while dragged; dragging onto a filled socket swaps back into the source slot, and dragging out chooses the
exact inventory or open-storage destination. Releasing a socket drag outside slots cancels. Changed source/selected
weapon state cancels the gesture. The server's new reliable `AttachmentMoveIntent` (65) compares both full item stacks,
checks socket compatibility and container deposit rules, and applies all changes atomically through `AttachmentTransfer`.
The existing item-stack saves already preserve both assembled weapons and detached parts; no save layout changed.
The old AttachIntent and REMOVE action remain compatible. Four `AttachmentTransferTests` cover transactions, refusals,
storage access, saves and network rollback; eight further `--ui-check` cases exercise real input and the shared rule.
Capture the selected weapon with `--view ui --screen attachments --shot <png>`. Sight reticles/eye-point alignment and the
weapon light remain separate work.

Broad gunplay feel (L21), new authored clips (L22), and a complete per-weapon animation/art pass remain outstanding.
The changes here fix disconnected existing animation paths and expose item pose data for visual calibration.

## Bed and sleeping-bag names (L12)

Tap E on an owned respawn point opens the shared interaction modal in text mode. Enter or SAVE submits; Escape,
CANCEL or a click outside cancels. Holding E retains the real PICK UP action. The nameable capability and the verb's
text-input property are shared interaction features; no item-name switch or disabled friend action was added.

The server rechecks ownership, reach, sight and the ordinary interaction gates at submission. Labels are trimmed,
normalized to Unicode NFC, and limited to 48 Unicode scalar values; controls, formatting characters and malformed
Unicode are refused. Path-looking labels remain plain text. Wire fields allow 192 UTF-8 bytes so 48 four-byte characters
are not silently truncated. Labels update nearby clients, the map and the respawn list, including late joins.
Thing save version 6 appends deployable names and still reads version 5; PlayerVersion 6 breathing data is unchanged.

The coordinated build succeeded and all three `RenameTests` passed (`.artifacts/repair-focused6.log`), covering the text
boundary, owner/reach rules, real retrieval, old/new saves, observer replication, map labels and death-screen labels.
Five additional actual-input `--ui-check` cases passed on build 10 and cover dialog editing, explicit submission,
Unicode and cancellation (45/45 checks total with attachments, `.artifacts/repair-ui-check2.log`). The rendered
`.artifacts/repair-ui-name.png` was inspected at 1600×900: text, limit hint and SAVE/CANCEL controls fit cleanly.

## Common damaged-target feedback (L24)

Every target adapter can now advertise optional current/maximum vitality, separate from the existing ship repair
field. Deployables and world props, building pieces/doors/cupboards, natural resources, living animals, and ship hulls
and damageable parts expose the same contract from their authoritative or already replicated state. No wire fields
were added for this feedback.
The integrated player-corpse body phase also exposes its remaining harvest health; converting it to a lootbag removes
that value. Living animal health remains distinct from animal-carcass harvest state.

The client queries the nearest solid target within eight metres. Healthy objects and terrain still occlude damaged
objects behind them; an open doorway's empty interaction plane is skipped. Full, destroyed, missing, or invalid health
does not show a bar. The ground HUD consumes one model value, with no family-specific UI branching, and draws the
target name, percent and health bar above the crosshair. Long names are shortened by measured font width without
cutting Unicode text elements; inventory, radial, death and piloting screens retain their current layouts.

All three `TargetHealthTests` passed in the coordinated build-10 focused run: distance/occlusion/refusals, all
advertised server families and ship components, and actual client damage/repair replication. The family test creates
its own authored barrel fixture so it does not depend on nearby generated loot. The rendered
`.artifacts/repair-ui-target-health.png` was inspected at 1600×900: the long title is ellipsized, the 42% value stays
clear, and the bar sits above the crosshair without overlap. Actual gameplay target acquisition still needs the
coordinated gameplay capture; the screenshot uses the UI lab's explicit health model.
Capture the HUD layout using `--view ui --screen target-health --shot <png>`; actual gameplay should additionally aim
at a damaged barrel, wall or door to verify the target query and bar together.

The attachment layout capture `.artifacts/repair-ui-attachments.png` was also inspected at 1600×900. It shows the
weapon's three supported sockets and the attached scope, with no clipped labels or overlap with inventory slots.
All three captures use the UI lab's backdrop/mannequin; they do not validate the in-game character art.
