# Held-item animation and authoring

Implementation started 15 October 2026 at the owner's request. This document describes the
shared FPS/TPS presentation work; the validation section records what has actually been checked.

**Reopened, 16 October 2026:** the owner rejected the previous visual result as nearly unchanged
and selected **rock and gathering tools: carry, grip and swing** as the first repair priority.
The workshop and technical checks below remain useful; the earlier assistant review did not
establish owner acceptance. Six rock, axe and pick profiles now have revised carry, finger fits
and swing curves, with current captures and checks below and in [DEVELOPMENT.md](DEVELOPMENT.md).
**Subsequent owner review, 16 October:** the owner rejects the latest result and says the workshop
is not usable for correcting it, including difficulty dragging through the UI. The six-tool pass
is not visually accepted. Technical checks below remain historical diagnostics, not quality approval.

Read-only follow-up found that reusable Quaternius clips already exist, including `chop` from
`TreeChopping_Loop`, but FPS resets the skeleton and solves arms to the profile-driven item path.
Profile events also bypass the normal TPS action clips. Proposed replacement: imported skeletal
clips drive the arms/item, with limited grip correction; prove one axe before wider integration.
The existing full-body chopping clip is a test candidate, not a proven first-person solution.
This replacement has not been implemented or approved as a final design.

## Contract

Gameplay remains in the engine-free simulation and its held-behaviour registries. Presentation
must never award damage, ammunition, gathered items, or consumables from an animation callback.
Item action timing is sampled using the simulation's tick durations. First-person arms and the
third-person body are separate presentations of the same item/action; they share timing and grip
definitions, while retaining separate camera and body-space motion.

Each held model has an explicit profile in `game/data/ground/held-presentations.json`. Profiles
contain first/third-person placement, right/left grips, wrist offsets, finger poses, elbow/shoulder
controls, and editable action keyframes. Related items start from appropriate family animations;
each item remains independently editable. Existing meshes, body appearances and clothing are
reused. These are authored transform/hand curves consumed by the character rigs, not newly
imported motion-capture clips.

The owner supplied the opening of [How to GET BETTER at BOW PVP Rust](https://youtu.be/8hteJtJt0_M)
on 15 October as a motion reference. The bow's target sequence is low sideways carry, a raised
right-canted aim with the left arm extended, the string hand drawn toward the face, then release
and recovery toward the next arrow. The reference supplies behavior and composition; no models,
animations, textures or sounds from that game are imported. Input buttons are not visible in the
reference, so the existing aim/draw and fire controls are retained.

Animation layers have an explicit order: authored item/action pose, grip and arm solving, then
small motion effects. An authored finger pose replaces the procedural finger curl for that hand.
The preview uses the same `ViewmodelView` and `CharacterView` as gameplay. A separate item-only
viewer must not define a different first-person placement.

Melee remains an aimed, server-authoritative reach test. Its presentation should show the contact
at the action's strike phase, use a plausible contact adjustment, and distinguish hits from misses.
Changing camera FOV or editing an arm pose must not change gameplay reach. Client prediction is
presentation only; the server's result is authoritative.

## Editor

Double-click [Edit-Held-Items.cmd](../Edit-Held-Items.cmd) in
`D:\GameDev\Projects\HollowUniverseABTestB`. It builds the client and opens the workshop on the
metal hatchet, without a server or save game. To start on the rock, run this from the project root:

```powershell
.\Edit-Held-Items.ps1 -Item rock
```

1. Select an item in **ITEM LIBRARY**, set **ACTION** to `idle`, and use **First person**.
   In **Placement**, change **Position · m** and **Rotation · degrees** to compose the carry.
   FPS position uses camera axes: **+X right, +Y up, −Z forward**; more negative Z moves it
   farther from the face. Rotation X/Y/Z rotates about those axes in degrees. **Move grip**
   exposes red X, green Y and blue +Z handles; use arms orbit to see depth, Shift for 10× finer
   dragging, and Ctrl to snap to centimetres. Placement moves the held assembly.
2. In **Hands**, choose **Right hand** or **Left / support hand**. **Grip offset** moves that
   hand relative to the item's grip socket, so its axes follow the socket rather than the screen.
   Refine wrist orientation, palm seat and handle radius, then use **Fit fingers to handle** to
   replace this hand's explicit curls with its fallback pose. Cylindrical and pistol grips use
   measured wrapping from the selected body's bones and current radius/seat; the pistol's index
   finger keeps its trigger pose. Support/open grips retain preset fallback curls rather than a
   measured handle wrap. This is undoable; changing radius or seat alone keeps explicit curls.
   Refine the resulting joints: each finger row is knuckle / middle / tip in degrees.
   Use **First-person arms · orbit** to inspect both sides;
   right-drag turns the orbit and the wheel changes its distance. Watch the displayed reach
   correction: the renderer can move an unreachable assembly, masking an overextended pose.
3. Select `swing`, then **Motion**. Scrub the timeline to inspect the wind-up, contact and recovery.
   Edit **First-person item motion** or **Third-person item motion** at the playhead; these are
   offsets from the base placement. Editing between keys inserts a key automatically. **Add /
   update key** records the sampled pose; only interior keys can move or be deleted. Animated
   hand/finger fields edit the selected action; empty finger channels inherit the base grip.
4. **Marker** jumps to the authored contact pose. **Play** (Space) reviews the whole action;
   reduce **Speed** to inspect it. Enable **Gameplay timing** to review its retiming to the
   simulation's strike tick. Moving the marker or changing the authored duration does not alter
   gameplay damage timing or reach. The target/contact solve is a visual preview, not a server hit.
5. Compare **First person**, **First-person arms · orbit**, and **Third person · orbit**, then
   another body/outfit. Check the carry again after the swing: FPS and TPS have separate placement
   and motion fields, while the base hand settings are shared.
6. **Save all** / Ctrl+S writes every edited profile to
   `game/data/ground/held-presentations.json`, validates it, and atomically keeps the previous file
   as `.bak`. Restart an already-running game to load it. Ctrl+Z / Ctrl+Y undo/redo the selected
   item's edits; switching items keeps unsaved edits. **Restore this item's saved profile** resets
   only that item and is undoable. **Reload** replaces all edits from disk and clears undo history;
   reload/close asks before discarding changes. A disk change since opening blocks saving, so avoid
   simultaneous workshop and external JSON edits.

FPS and arms-orbit always show **palm / grip gap**, **wrist bend** and **reach correction**.
The gap measures the actual solved primary palm seat against the item's primary grip, including
authored offsets; a wrist reaching its own target can still leave a palm gap. Wrist bend is the
angle between the solved forearm and wrist-to-knuckle direction, with 0° straight. It is a diagnostic,
not an anatomical acceptance test. Reach correction is how far the renderer shifted the authored
assembly to make it reachable. These numbers do not establish finger contact or visual quality.

Current limitation: for a one-handed item, the free left-hand motion position/rotation fields are
not consumed by the FPS pose. TPS has no measured wrist/palm attachment guarantee; inspect it
visually. The new diagnostics and fitting control passed the six-tool checks recorded below;
those checks do not establish finished animation quality.

## Timing and authority

`HeldActionTiming` is engine-free. Melee strike and swing duration, reload and bow draw use the
same rounded tick counts as gameplay. An artist's event key is remapped to that tick without
changing damage, reach, fire rate, ammunition or cooldowns. The metal hatchet's 0.28-second data
value becomes tick 8 at 30 Hz (0.267 seconds); both views use that contact phase.

Modeled throw, repair, medicine, food and placement actions have a cancellable local wind-up,
with fixed tick delays defined in `HeldActionTiming`, followed by the existing validated server
intent. Changing art duration or moving its marker cannot change that delay. Switching items or
slots, entering a seat, death/wounding, and unavailable hands cancel uncommitted actions. Ghost-only
placeables keep their ordinary placement path. Inventory actions retain their existing rules.

Replicated action state carries the item ID and action tick, so consuming the final item does not
erase the model before its animation finishes. Remote instant-action presentations start at the
commit phase after server acceptance. Hit markers carry the authoritative contact point, weapon
and swing tick; stale or unrelated markers cannot make a new swing recoil. Misses have no impact
feedback. Melee is still one thick aimed reach test at the strike tick, not collision against a
camera-scaled visual axe mesh.

Changing belt slots sends an explicit cancellation to observers even when both slots hold the
same item type. Depleting the final stack in the current slot does not emit this cancellation.
Network-timestamped FPS/TPS actions seek to elapsed time without advancing the same frame twice.

The server's character presentation mapping lives in `Hollow.Server/CharacterPresentation.cs`;
the host retains snapshot scheduling. FPS and TPS use separate rig instances from the existing
character assets, with hands/forearms masked for FPS and a full body for TPS. This change does not
claim a new dedicated high-detail arms mesh or imported animation library.

The Frontier rifle import now splits the owned source's rigid Magazine and Bolt groups out of
the static pickup at all three LODs. Their triangles are removed from Body, preserving the seated
assembly, materials, UVs, sockets and collision hulls. FPS and TPS move the actual magazine with
the solved supporting hand during extraction/insertion, then return it to its seated transform.
The importer and its verification record make this reproducible; no original source file is edited.

## Reproducing checks

After building, launch Godot with `--path game -- --view held-editor --editor-check` for actual UI
edits, history, keyframe operations, gizmo dragging, temporary save/reload, and all-item FPS/TPS
sampling. It writes no production profile changes. `--editor-check-only metal-hatchet,bow` narrows
a diagnostic run. `--editor-sheet ../docs/images/held-items/axe-swing.png --item metal-hatchet
--action swing` captures both views at five action phases. `--body female --outfit 4` checks a
body/clothing variant. `--shot PATH` captures the whole editor.

The check also reports maximum FPS primary-palm gap, wrist bend and authored reach correction
per action. Optional `--editor-check-max-reach <metres>`, `--editor-check-max-palm <metres>` and
`--editor-check-max-wrist <degrees>` fail the selected run when a sampled value exceeds that
limit; combine with `--editor-check-only rock,metal-hatchet,metal-pickaxe` to scope it. No limit
for these three diagnostics is enforced unless its flag is supplied. Palm/wrist maxima include
only phases when the primary hand should remain attached. These are sampled diagnostic bounds,
not continuous-motion or TPS guarantees.

`--editor-check-grip-margin 0.06` additionally bounds the sampled FPS primary-grip origin inside
the middle 88% of each image axis during `idle` and `swing`. This uses the viewmodel projection;
it does not measure the complete hand/tool silhouette or continuous motion. Inspect frames too.

For a motion recording, `--item bow --editor-cycle bow --editor-animation-dir PATH` exports the
actual preview at 1280×720 and 24 fps: carry, draw, release, recovery. The destination must be
empty. Numbered PNGs and `manifest.json` record the sampled timings; use `--camera tps` for the
body view, or `--action reload` without a cycle for a single action. Exports are capped at ten
seconds. `--ads` retains aim after release; `--game-timing` applies simulation timing.

The editor check samples authored keys, segment midpoints and the event pose, and checks the
actual solved wrist against both required hand targets. A holding hand farther than 25 mm from
its target fails the check. Intentional throw release and bow string release are excluded only
while that hand is meant to let go. Authored reach correction is reported separately, even when
attachment passes; it must still be reviewed visually.

## Completion checks

- Every current held model has a valid profile and the actions its behaviour needs.
- Save/reload and undo preserve the edited pose; invalid data does not replace a valid file.
- FPS and TPS preview and gameplay use the same profile and tick-based action durations.
- Grip remains attached through movement, action, aim and recovery, including body/clothing variants.
- Contact timing is checked at close, reachable and out-of-reach distances and during cancellation.
- Guns, bows, throwables, medicine, food and building tools retain their distinct gameplay actions.
- Solution build and engine-free tests pass; actual rendered previews are captured and inspected.

## Validation

### Six-tool repair candidate — 16 October 2026

Rock, stone/metal hatchets, salvaged axe and stone/metal pickaxes now have separate carry and
wind-up/contact/recovery poses, with measured primary-hand finger fits. Only these six profiles
changed; every authored duration and marker is preserved. Gameplay reach and damage timing are
unchanged. The first attempt was rejected during review for cropped hands; the retained revision
shows the holding fist through the sampled swings and reduces the hanging thumb/hooked fingers.

- Solution build: zero warnings/errors. Focused Held/Content/Architecture checks: **27/27** in
  21.6 s. An earlier shared full run passed 389/389 before the later held diagnostic/test edits;
  it is not a final full-suite run for this revision.
- Final male and female runs each pass **24 actions / 404 FPS/TPS samples**, including actual
  finger-fit button/undo and a displaced-palm regression. Requested limits: 8 cm authored reach
  correction, 25 mm palm gap, 65° wrist diagnostic and 6% primary-grip frame margin in idle/swing.
  Maximum reach correction is 0 cm male / 7.11 cm female; maximum wrist diagnostic is
  61.6° / 64.6°. Palm gaps round to 0.0 mm. TPS is checked for finite transforms and inspected
  visually, without a measured contact guarantee. Logs: `.artifacts/held-tools-review/review-male.log`,
  `review-female.log`, `build-final.log` and `focused-tests.log`.
- Six FPS/TPS sheets, a clothed female pick sheet and three 1280×720 gameplay-timed swing
  sequences were rendered and inspected. [Workshop](images/held-items/2026-10-16/workshop.png),
  [rock](images/held-items/2026-10-16/rock.png), [axe](images/held-items/2026-10-16/metal-hatchet.png),
  [pick](images/held-items/2026-10-16/metal-pickaxe.png),
  [female pick](images/held-items/2026-10-16/female-pick.png).
  [Rock recording](images/held-items/2026-10-16/rock-swing.mp4),
  [axe recording](images/held-items/2026-10-16/metal-hatchet-swing.mp4),
  [pick recording](images/held-items/2026-10-16/metal-pickaxe-swing.mp4) repeat each sampled cycle
  four times at 24 fps; they are workshop playback through the gameplay rigs, not a hosted combat test.

The owner subsequently rejected this candidate. Recorded review concerns include the salvaged axe head leaving the top of
the frame during wind-up and the metal axe's thin strike silhouette. Free-left FPS motion remains
unsupported. Existing ObjectDB/Jolt exit warnings remain. This does not close all 41 profiles.

### Historical implementation evidence — 15 October 2026

The following evidence was captured from the integrated AB Test B working tree on 15 October 2026.
The combined repair session owns the final integration commit; these checks describe the held-item
implementation and do not certify unrelated work added after this checkpoint. The visual inspections
were assistant judgments; the owner's rejection on 16 October reopens visual quality despite the
technical passes below. They are historical evidence, not validation of the new repair.

- The integrated engine-free suite passed **376/376** (`.artifacts/repair-full2.log`), including
  real two-client hit markers and switching between identical guns during reload. The focused
  presentation/timing/model run passed 12/12, including the actual rifle magazine/LOD regression.
  Shared build 13 passed with zero warnings/errors (`.artifacts/repair-build13.log`).
- All 41 profiles and 175 actions passed the editor's actual numeric edit, live preview,
  undo/redo, key editing, gizmo drag, focused-field shortcut and temporary save/reload checks.
  Male and clothed female runs each sampled 2,762 FPS/TPS poses. No required FPS holding hand
  exceeded the 25 mm attachment tolerance. TPS transforms were finite; this is not a measured
  TPS wrist-distance guarantee.
- Captured and inspected the all-item carry sheet and FPS/TPS axe, pick, bow, pistol, rifle,
  bandage and grenade action sheets. The bow sequence is also a 51-frame, 24 fps recording.
- Imported the rifle's real Magazine/Bolt parts and checked actual magazine extraction/insertion
  in both views. After the final reload reach correction, male and clothed female gun subsets
  each passed 12 actions / 200 FPS/TPS samples, including magazine presence and carrying phases
  (`.artifacts/held-reload-final2-male.log`, `held-reload-final2-female.log`). Inspected all 56 frames
  of the final rifle recording, including the previously failing 18% and 89% poses. The weapon
  stays in view through reload; the earlier camera-ward flips are absent. Reload now prefers the
  nearest reachable pose, with smooth transitions back to the ordinary grip at the ends.
- Adaptive reach corrections remain visible in the workshop, especially for shorter body
  proportions and extreme throw/swing poses. The correction is continuous and keeps normal
  limb lengths; a zero hand error does not mean the originally authored pose was reachable.
- Automated viewers report one `ObjectDB` instance at shutdown. The final pistol/rifle
  subset also reports six leaked Jolt shapes at exit. Behavioural checks pass, but these
  shutdown warnings remain unresolved and are not being represented as a clean shutdown.
  The same warnings occur in captures before the rifle asset split. The magazine controller
  only moves existing nodes and adds no collision shapes; the precise leaked owners have
  not been established.

Visual evidence: [workshop](images/held-items/editor.png),
[all held items](images/held-items/all-items-fps.png),
[axe](images/held-items/axe-swing.png), [pick](images/held-items/pick-swing.png),
[bow draw](images/held-items/bow-draw.png), [bow cycle](images/held-items/bow-cycle.mp4),
[rifle reload](images/held-items/rifle-reload.mp4).
The rifle export's static verification is recorded in
[held-rifle-parts.json](reports/held-rifle-parts.json).
