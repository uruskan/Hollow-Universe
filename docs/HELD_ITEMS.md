# Held-item animation and authoring

Implementation started 15 October 2026 at the owner's request. This document describes the
shared FPS/TPS presentation work; the validation section records what has actually been checked.

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

Double-click `Edit-Held-Items.cmd` in the project root. It builds the C# client and opens a
standalone workshop with no server or save-game dependency. PowerShell also accepts
`./Edit-Held-Items.ps1 -Item bow`. Saved profiles are project content; restart an already-running
game to load saved changes.

1. Search the item library and select an item. Choose a body, outfit, and FPS, arms-orbit, or TPS view.
2. **Placement** changes the base grip position/orientation, TPS attachment offset, swing pivot,
   and the named contact socket. **Move grip** exposes draggable RGB translation axes in FPS/orbit;
   Shift gives fine movement and Ctrl snaps to centimetres. Right-drag orbits, and the wheel zooms.
3. **Hands** selects either hand and edits its socket offset, wrist, elbow pole, shoulder, palm seat,
   handle radius, and fifteen finger-joint curls. Grip offsets move the hand relative to the item;
   placement moves the held assembly in view.
   Aim placement and bow string travel are editable too. If a pose asks the arms to reach too far,
   the renderer moves the held assembly within reach and the workshop displays the correction.
4. Select an action at the bottom and scrub the timeline. **Motion** edits FPS/TPS motion and each
   hand's offsets/finger poses. Editing between keys inserts a key. Add/update, move, or delete
   keys; endpoints stay at 0 and 1. Finger keys can inherit the rest grip.
5. Play to review anticipation, event, and recovery. **Gameplay timing** retimes the event pose
   to the simulation clock. The target and reach guide are previews, not a server hit. The contact
   solve toggle demonstrates the small visual adjustment toward a target.
6. **Save all** or Ctrl+S writes validated JSON atomically, retaining the previous file as `.bak`.
   Ctrl+Z/Ctrl+Y undo/redo; item switching retains each item's edits. External file changes are
   detected before saving. Reload/close asks before discarding unsaved changes.

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

The following evidence was captured from the integrated AB Test B working tree on 15 October 2026.
The combined repair session owns the final integration commit; these checks describe the held-item
implementation and do not certify unrelated work added after this checkpoint.

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
