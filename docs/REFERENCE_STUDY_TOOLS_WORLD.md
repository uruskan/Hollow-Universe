# Tool handling and world presentation reference study

17 October 2026. This study translates the owner's two videos and two screenshots into
specific presentation targets for Hollow Universe AB Test B. The metal hatchet and metal
pickaxe **holding poses in the supplied screenshots are accepted baselines**. That does
not establish acceptance of their equip, locomotion, swing, impact or recovery animations.
Other tools need individual review and correction; changing every tool by one rotation
would miss both the geometry differences and the different hand performances they need.

The most immediate implementation issue is that source-bound tools skip the existing
walking/sway/sprint presentation. Several other tools still use the older item-curve/IK
path. A convincing repair must address the complete hand–wrist–forearm–tool performance,
then judge it in motion and against actual contact, rather than pass a grip-distance test
or improve only an idle screenshot.

## References and review coverage

| ID | Reference | Purpose |
| --- | --- | --- |
| VID-TOOLS-01 | [Enurfase, HOW TO chop trees in RUST](https://www.youtube.com/watch?v=JTTJlCEwfUE) | Continuous ordinary-play carry, turns, traversal, repeated axe strokes, tree feedback and departure. |
| VID-WORLD-01 | [Facepunch, Rust Official Trailer](https://www.youtube.com/watch?v=LGcECozNXEw) | Tool/weapon hand choreography, terrain, materials, lighting, construction, animals, combat, destruction and traversal. |
| IMG-TOOLS-01 | [Owner's metal hatchet capture](images/tool-reference-baselines/accepted-metal-hatchet.png) | Accepted current hold, angle and apparent scale. |
| IMG-TOOLS-02 | [Owner's metal pickaxe capture](images/tool-reference-baselines/accepted-metal-pickaxe.png) | Accepted current hold, angle and apparent scale. |

The review covered **1,233 frames at 30 fps** in VID-TOOLS-01 and **9,229 frames at 60 fps**
in VID-WORLD-01: **10,462 decoded frames**, in 106 consecutive contact sheets without temporal
sampling gaps. Every sheet was inspected in chronological reading order. The sheets use
192 × 108 thumbnails; 28 selected trailer frames were also inspected at 1280 × 720, and
36 consecutive frames of the first axe stroke were enlarged to 480 × 270.

This is exhaustive **temporal coverage at thumbnail scale**, not a claim that every pixel
of every frame was examined at full resolution. Fine finger intersections, exact FOV,
world-unit measurements, simulation rules and movement inputs cannot be recovered reliably
from these videos. Trailer cuts and cinematic views are not measurements of ordinary play.
The two decoded visual durations are 41.100 and 153.817 seconds; container/audio duration
can differ slightly. Frame times below are video observations, not engine event times.

**Audio listening remains incomplete.** An extracted audio segment was presented to the
available interface, which explicitly reported that audio input is unsupported. Captions
and the game's sound-routing code were reviewed; neither establishes timbre, mix quality,
spatial perception or audible synchronization. Audio recommendations below are identified
as proposals, and the narrator's advice is attributed to captions.

Reference video, extracted frames and sound remain outside the repository in the temporary
study directory. Only the owner's own Hollow Universe screenshots and written observations
are stored here. No Rust model, texture, animation or sound becomes a game asset.

Detailed trailer ledgers, including transitional, title and black frames:

- [Opening through exploration, 0–50 seconds](reference-studies/rust-trailer-00-50.md).
- [Night, combat, teamwork and construction, 50–100 seconds](reference-studies/rust-trailer-50-100.md).
- [Raids, vehicles and closing sequence, 100 seconds–end](reference-studies/rust-trailer-100-end.md).

## Continuous axe clip

| Time | Observation | Lesson for our game |
| --- | --- | --- |
| 0.00–3.30 | One right hand supports the lower handle; the broad head is high/right of the central traversal view. Small pitch/yaw changes alter the background much more than the settled grip. | Establish a stable carry silhouette and a plausible wrist. The hand, handle and head must read as one assembly. |
| 3.33–9.97 | Player looks around and shifts among nearby birches. Trees lean differently; ferns, low plants, stones, bare earth and dappled light form uneven patches. | Look response must settle without loose sliding. Ground interest comes from varied placement and relief, not equal density everywhere. |
| 10.00–18.30 | Long stationary/near-stationary inspection of already marked trees, punctuated by turns and short approaches. The axe remains usable and recognizable. | Quiet carry deserves as much scrutiny as the swing. Avoid continuous exaggerated movement while the player is standing. |
| 18.33–25.70 | Repositioning, then a steady view toward a red X on a nearby trunk. The target and the tool are distinguishable. | Carry should leave the intended contact area visible. A target marker should occupy the target surface rather than float independently. |
| 25.73–25.90 | First stroke: head exits high/right, the forearm crosses, then the narrow working view of the head moves into the trunk. | Use preparation followed by rapid inward travel. Rotating the tool independently from the hand would lose this performance. |
| 25.93–26.13 | Head is close to the marked trunk; chips and a dusty/colored burst appear at about 25.97. Handle is nearly vertical in the contact composition. | Working edge, contact effects and target state must agree spatially. Do not merely move an arm while the hit appears elsewhere. |
| 26.17–26.70 | Head recoils up/right, leaving the center clear while particles fall and fade; hand/tool move outside the view for part of recovery. | Brief intentional offscreen motion is valid. The important criteria are readable contact and return, not forcing every frame inside a screen-margin box. |
| 26.73–27.30 | Next stroke and chips around 26.90–27.00, then withdrawal/turn. | The action has fast travel and a slower return; it is not a constant-speed pendulum. Approximate visible impact spacing here is about 0.93 s, not a proposed gameplay cooldown. |
| 27.33–28.13 | Fast travel to the next trunk; tool drops low, substantially clearing the forward view. | Traversal carry must differ from the working stance, with a transition rather than an abrupt snap. The input mode is not visible, so this alone does not prove a sprint-key state. |
| 28.17–29.63 | Chops on another tree, local chips and a large felling/dust response; then movement toward another target. | Separate ordinary hit, weak-spot response and depletion. Residual world effects may continue while the player moves away. |
| 29.67–33.73 | Repeated approach/chop/recovery at another birch, with substantial dust from adjacent falling trees. | Effects must remain located in the world and readable through consecutive actions. Repetition should not restart unrelated clouds or detach the hand. |
| 33.77–36.60 | Turn, low traversal carry, more chops and tree response. | Validate chaining between locomotion and strikes, including moving and turning immediately after impact. |
| 36.63–38.40 | Player moves toward the nearby horse; camera lowers/rises and turns during approach. | Environmental animation and props create a continuing world around the gathering action. |
| 38.43–41.07 | Mounted viewpoint with horse ears in foreground; axe returns to a settled carry as travel continues. | This is mounted motion, not an on-foot running reference. Do not fit the on-foot bob curve from these last seconds. |

### What the first clip adds beyond the swing

The auto-generated captions around [10–24 seconds](https://www.youtube.com/watch?v=JTTJlCEwfUE&t=10s)
describe preparing multiple trees until their near-fall creak, then finishing them quickly
and departing to reduce the time exposed to conspicuous tree sounds. This is the speaker's
advice, not a measured audibility result. It identifies a useful design principle: gathering
audio can convey resource state **and** expose the gatherer to other players. The proposed
audio review should therefore include near-depletion warning, collapse and distance/occlusion,
not only whether an isolated axe impact sounds attractive.

The enlarged first stroke changes from the broad side of the head in carry to a narrow
working view at the trunk, then withdraws. Screen position alone cannot reproduce that:
edge orientation, wrist orientation and the path into/out of contact must be designed together.
The reference also permits a brief invisible windup/recovery. It does not justify a tool
remaining offscreen for most of its useful action or hiding its contact frame.

## Trailer lessons by concern

These are observations followed by proposed applications. The reference does not authorize
adding every depicted vehicle, opponent, event or mechanic. [DESIGN.md](DESIGN.md) remains
authoritative, including the existing ground UI, original assets and Subsistence atmosphere.

| Concern | Useful reference interval | Observation and proposed application |
| --- | --- | --- |
| Body weight and starting state | [3.75–7.45](https://www.youtube.com/watch?v=LGcECozNXEw&t=4s) | Beach survivor bends, gathers legs, crouches and stands. Pose changes need support and weight transfer; a body translated vertically does not communicate standing up. |
| Rock | [7.47–9.75; 11.88–16.72](https://www.youtube.com/watch?v=LGcECozNXEw&t=7s) | Two hands support its volume, including stable carry. The left hand releases/reseats during equip. Lift, contact and recovery differ. Give the rock its own two-hand performance rather than a small axe pose. |
| Stone tools | [16.73–21.28](https://www.youtube.com/watch?v=LGcECozNXEw&t=17s) | Inspection displays the head/lashings across the screen; mining and chopping use forceful inward travel with compact contact and rightward recovery. Inspection framing is not idle framing. |
| Axe contact | [19.82–21.28](https://www.youtube.com/watch?v=LGcECozNXEw&t=19s) | Contacts near 20.05 and 20.93 align head, pale damaged bark, X, chips and dust. The right forearm approaches diagonally from the lower right. Use actual gameplay contact captures to check this agreement. |
| Resource depletion | [14.40–16.72; 21.30–22.58](https://www.youtube.com/watch?v=LGcECozNXEw&t=14s) | Node silhouette shrinks after a hit; a tree tips and accelerates through the view. State changes should alter the world, with bounded effects supporting the change. |
| Bow | [31.55–33.15; 35.33–38.60; 58–61](https://www.youtube.com/watch?v=LGcECozNXEw&t=35s) | Equip, low carry, nock, raise/draw, release and recovery are distinct. Left arm and canted bow leave the target line open. Preserve independent support-hand tasks. |
| Spear and animal threat | [38.62–43.82](https://www.youtube.com/watch?v=LGcECozNXEw&t=38s) | Forward thrust responds to a closing wolf; bear closes while bow preparation consumes time. Range and readiness should read through animation and target motion. Do not derive exact AI or damage rules from edited encounters. |
| Armed locomotion | [46.07–47.72; 70.52–73.30; 105.25–108.45](https://www.youtube.com/watch?v=LGcECozNXEw&t=70s) | Torso, arms and legs share the motion while weapon support remains stable. Evaluate external body views as well as FPS arms; locomotion and aiming need compatible layers. |
| Reload and handling | [66.75–67.83; 103.25–105.23; 111.02–112.97](https://www.youtube.com/watch?v=LGcECozNXEw&t=66s) | Primary hand retains the grip while support hand manipulates the assembly. Mesh-only rotation cannot reproduce contact, bracing and hand return. |
| Bulky held equipment | [112.98–115.90](https://www.youtube.com/watch?v=LGcECozNXEw&t=113s) | Launcher changes from low carry to shoulder/offset sight, then recoils and clears smoke. Screen occupancy and bracing must depend on the item's size and function. |
| Ground at three scales | [0–7; 31–43; 75–84; 105–108](https://www.youtube.com/watch?v=LGcECozNXEw&t=107s) | Large ridges/valleys, medium vegetation masses/open routes, small stones/flowers/litter. Build these scales separately and inspect at walking height. More tiny grass alone cannot fix monotonous terrain. |
| Woodland composition | [0–21; 41–44; 92–94](https://www.youtube.com/watch?v=LGcECozNXEw&t=41s) | Different trunk colors, lean, crown sizes, dead branches, ferns, stones, local relief and clear gaps. Compare our repeated thick striped trunks and uniform floor against this hierarchy. |
| Shores and water | [1–7; 94.67–100; 132.48–133.25](https://www.youtube.com/watch?v=LGcECozNXEw&t=94s) | Wet/dry shore bands, pebbles, reeds, broad waves, small highlights and vessel wakes. Material transitions and disturbed water explain location and motion. |
| Biomes | [16–21; 75–84; 115.92–120.95](https://www.youtube.com/watch?v=LGcECozNXEw&t=116s) | Brown understory, dry scrub/palms and patchy snow around bare concrete have distinct distributions. A uniform texture/color swap is insufficient. |
| Daylight and distance | [44–51; 107–108](https://www.youtube.com/watch?v=LGcECozNXEw&t=47s) | Nearby occlusion, sunlit middle distances and paler far silhouettes create depth. Preserve readable shadows and landmarks while avoiding cinematic glare that obscures gameplay. |
| Night and local light | [51–57; 120.97–126.60; 138–146](https://www.youtube.com/watch?v=LGcECozNXEw&t=51s) | Torch/flashlight reveal limited regions; muzzle flashes warm nearby surfaces briefly; fire persistently changes room visibility. Use cool background silhouettes, persistent practical lights and short action light as separate layers. |
| Materials | [17.40; 26–31; 81–94; 109–113](https://www.youtube.com/watch?v=LGcECozNXEw&t=26s) | Stone, rope lashings, fabric folds, log ends, braces, corrugated metal and exposed mechanisms remain distinguishable. Surface detail must agree with geometry, scale and use. Handmade does not require all assets to be equally dirty. |
| Building and placement | [22.82–31.53; 84.03–89.28](https://www.youtube.com/watch?v=LGcECozNXEw&t=23s) | Readable preview shape, valid/blocked color, physical placement, short completion burst and material change communicate cause/effect. Gates actually rotate and progressively occlude the opening. Preserve our confirmed UI. |
| Combat feedback | [78–84; 92–94; 108–126](https://www.youtube.com/watch?v=LGcECozNXEw&t=92s) | Flash, recoil, casing, moving tracer, impact, dust/smoke and fire have different lifetimes. Bind each to its actual cause and location; keep aim and nearby geometry readable. |
| Destruction | [112.5 onward](https://www.youtube.com/watch?v=LGcECozNXEw&t=112s) | Panels and beams fly; openings, cover, illumination and visibility change. Damage is more than a health bar or a disappearing intact box. |
| Monuments and navigation | [44–51; 62–67; 97–105; 119–120; 129–137](https://www.youtube.com/watch?v=LGcECozNXEw&t=44s) | Roads, towers, large silhouettes, repeated structural modules and vertically separated routes establish direction and scale. Foreground cover, subject and background landmark should remain distinguishable. |
| World life | [33–44; 55–57; 94–100; 126–137; 138–146](https://www.youtube.com/watch?v=LGcECozNXEw&t=138s) | Animal adjustments, swimming propulsion, wakes, smoke, moving vehicles and quiet campfire motion continue around player actions. Choose restrained, purposeful secondary motion instead of making everything equally animated. |

The trailer's hidden HUD, captions, branding, cinematic camera positions, cuts, depth of
field and motion blur are not reasons to remove the ground UI or reduce aiming clarity.
It is a strong composition reference, not proof of exact performance, networking or balance.

## Current implementation audit

Audited the working checkout on 17 October, including its pre-existing uncommitted changes.
These are source findings; the study did not run or change game behavior.

| Area | Current finding | Consequence |
| --- | --- | --- |
| Accepted metals | Metal hatchet and metal pickaxe use skeletal `chop` with the same complete-rig offset: position `(-.04,-.14,-.25)`, rotation `(25,0,0)`. | Keep the accepted rest composition as the regression baseline. |
| Stone pickaxe | Already uses the same binding/composition/grip as the metals. Its model's tip is approximately `(0.230,0.270,0)` from grip, versus metal pick `(0.235,0.410,0)`. | Copying the metal profile again will not fix it. Judge the shorter head/handle geometry and tune its own profile. |
| Stone/salvaged axes | Still follow legacy item transforms plus arm IK; stone axe has a different complete-rig translation. | A source-motion candidate may be useful, but each model needs its own grip and composition review. |
| Other tools | Rock, torch, knife, spear and hammer have different purposes and proportions. | They need distinct carry/actions; the axe chop is not a universal replacement. |
| Locomotion | `PoseSkeletalMotion()` sets a static rig composition and wrist attachment; `Pose()` skips the procedural placement branch. Bob/sway live in that skipped branch. Idle/equip/sprint source bindings hold one pose. | The source-bound tools lack the requested moving carry and transitions. Apply any new motion to the complete arms/item assembly, with identity at rest. |
| Contact | Skeletal playback skips the procedural contact adaptation. | A correctly attached item can still miss the visible target. Verify working-end alignment against real authoritative hits without changing reach to disguise a visual error. |
| Swing sound | `FirstPersonView.Swing()` and remote `PeopleView` play swish at action start. | There is no authored fast-travel sound marker. A marker is a proposed change requiring actual audio/video verification. |
| Natural impact audio | `NatureView.React()` uses fresh replicated natural-state hits and target `gather.json` cues. Per-item `hitWood`/`hitStone` sound roles do not choose those impacts. | Editing those item sound roles alone cannot distinguish a rock hitting wood from an axe hitting wood. Extend the shared contact metadata if that distinction is adopted. |
| Timing/authority | Existing action timing maps the presentation marker to the server's strike; confirmed response matches the active attack identity. | Preserve these guarantees. Animation/audio callbacks must not grant damage or resources. |

Source entry points:

- [Held profiles](../game/data/ground/held-presentations.json),
  [skeletal FPS composition](../game/scripts/Characters/ViewmodelView.SkeletalMotion.cs),
  [FPS placement and routing](../game/scripts/Characters/ViewmodelView.cs),
  [procedural contact response](../game/scripts/Characters/HeldPresentationViewmodel.cs).
- [Local presentation/audio](../game/scripts/Ground/FirstPersonView.cs),
  [remote presentation/audio](../game/scripts/Ground/PeopleView.cs),
  [natural contact cues](../game/scripts/Ground/NatureView.cs),
  [gathering data](../game/data/ground/gather.json),
  [audio recipes](../art/audio/events.py).
- [Existing held-item contract](HELD_ITEMS.md) and
  [P4 validation limits](repair-results/P4-first-person.md). Historical notes that say only
  the metal hatchet uses the source path are superseded by this current audit.

## Proposed repair sequence and acceptance

1. **Preserve and compare the accepted holds.** Capture identical body, FOV, aspect, item
   and camera conditions. Baseline both metal tools at rest. Do not treat an old grip-test
   pass as user acceptance of other tools.
2. **Fix tool composition individually.** Stone/salvaged axes may start from the accepted
   source performance; stone pick needs a geometry-specific review; rock needs volume
   support, knife a suitable blade grip, spear a forward thrust and hammer an appropriate
   short working stroke. Keep profiles data-driven and avoid item-name condition chains.
3. **Add coherent movement.** Optional complete-rig locomotion composition should blend
   idle/start/walk/run/stop/turn, retain a fixed baseline at rest, and attenuate during
   authored strikes. Keep wrist/source rotations intact. Sync against actual distance/
   footstep cadence rather than inventing a disconnected decorative oscillator.
4. **Judge contact and repetition.** Show preparation, fast strike, contact, recoil and
   recovery at real speed and frame by frame. Test first swing, held repeat, fast taps,
   turn during recovery, switch/cancel, close/normal/maximum range and misses. Effects must
   originate at the accepted contact; misses must not produce impact confirmation.
5. **Perform the unresolved listening pass.** Compare original recordings at matched level:
   swish on fast travel, dry wood contact, heavier body/resonance, small debris tail,
   stone/ore distinctions, near-fall warning and collapse, footsteps under each gait,
   environmental bed, distance and occlusion. These are evaluation dimensions, not claims
   that the reference has a specific frequency balance or that our current sounds fail it.
   Use original/licensed sources and the existing sound pipeline. Avoid duplicate predicted
   and authoritative impact cues. Caption-supported noise exposure needs gameplay review.
6. **Run a separate environment comparison.** Start at walking height with the same sun/time
   and route. Compare trunk scale/bark frequency, tree grouping, ground relief, openings,
   grass height/density variation, undergrowth, material transitions, shadow readability
   and distant forms. Change one concern at a time and measure cost. Trailer observation
   alone is not justification for a wholesale deterministic terrain rewrite.

Acceptance evidence must include male and female arms, FPS and external views, 16:9 plus
4:3 and 21:9 framing, real-time recordings with sound, and diagnostic contact frames.
Brief offscreen preparation can be deliberate; wrong contact orientation, disconnected
hands, a hidden working phase or failure to return to the accepted hold is not acceptable.
Retain source-fidelity and grip checks, but add human visual judgment to those checks.

Relevant existing checks include `HeldPresentation`, `HeldActionTiming`, `AttackMelee`,
`Architecture`, `Content`, `AudioRepair` and the fresh-gather-hit network test, plus the
held editor's source-fidelity and attachment diagnostics. Build before testing runtime
changes and use coordinated Godot captures. This study is documentation and reference
analysis only: **no pose repair, audio repair, terrain change or runtime acceptance is
claimed by it**.
