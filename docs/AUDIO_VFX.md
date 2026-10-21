# Shared contact feedback and resource budgets

The owner authorized this architecture pass on 18 October 2026. It retains the existing
damage/target registries, attack lifecycle, audio director, VFX director and gib renderer.
The architecture below is implemented in this checkout; validation results are recorded
separately in DEVELOPMENT.md so implementation is not mistaken for runtime acceptance.

The server captures `SurfaceContact` at collision time, before damage can remove the
target. It contains a physical surface ID, target identity/part, point and normal, and
an optional target-relative anchor. Registered receivers provide collision evidence;
registered target handlers resolve material and attachment pose. Projectile terminal
messages preserve this record through authoritative/predicted reconciliation, including
terminal-before-launch delivery. Presentation never re-identifies an impact by proximity
or guesses a deployable material from its item name.

`game/data/ground/contact-surfaces.json` owns stable physical IDs and mappings for terrain,
building grades, deployables, doors, natural kinds, ships and optional ship/deployable
part overrides (`content-key/part-byte`). ID zero is
explicitly unknown. `contact-responses.json` maps that shared identity to action responses,
footstep banks and dust colour. Bullet/arrow actions choose existing sound and effect
keys; optional action modifiers such as incendiary rounds are overrides in the table.
These are presentation choices, not physical rules. Distinct glass, cloth, gravel and
snow identities currently reuse some existing impact assets explicitly in the table.

`ContactResponseData`, physical materials, contact records and cue timelines are
engine-free. Godot resource adapters use `FileAccess` so data also loads from exported
packages. Add a new material by declaring its identity, target mapping and complete
responses; validation rejects missing references. Renderer-specific resources are never
sent over the network.

The client consumes each endpoint through its existing attack/projectile lifecycle.
For attached contacts it resolves the current pose through the existing target registry.
Renderer-owned pose registrations supply intermediate animated poses for presentation;
gameplay aim and collision continue to use their existing authoritative geometry.
Pooled decals retain a local transform and bounded per-decal pose callback, follow moving
parts and retire when their target disappears. Transient particles remain free after
emission. Blood/flinch suppression uses bounded per-world victim identity rather than a
single global nearby position.

Footstep cadence and footwear remain independent shared concerns. Floor/terrain and ship
part materials now feed the same physical material and response tables. Movement still
owns when a contact exists; footwear owns which recorded bank plays that contact.
`action-cues.json` owns normalized reload cue times. Local and remote presentations use
the same engine-free timeline and gameplay duration, skip elapsed cues on late joins,
and cancel owned timelines on action changes. Successful repair audio is gated by the
accepted intent; refusals, expired and duplicate answers do not play it.

Audio loops have persistent logical handles separate from scarce Godot voices. An
inaudible/distant loop retains its lifecycle without reserving a player, and approaching
or unmuted sources can reacquire a voice after contention. Same-emitter/event registration
is idempotent. Voices obey global and per-event limits with audibility/priority selection;
gain, pitch, fade, stop and freed-emitter cleanup also apply while virtual. Configuration
is the `voiceBudget` section of sounds.json, preserved by the offline audio builder.
Physical playback and occlusion are bounded by voice counts. Logical metadata and the
periodic audibility scan scale with live owned emitters and are released with them.
Defaults allow 56 spatial and 20 flat voices, with loop selection at 20 Hz. Loop-mode
stream copies are explicitly owned and disposed after the final director releases its
players; imported shared streams remain owned by Godot's resource cache.

VFX uses one strict per-renderer-frame particle budget across bursts, delayed layers and
continuous emitters. `fx-budgets.json` bounds active loops, flame meshes and loop lights;
per-effect catalogue caps participate in selection. Logical loop handles persist while
culled and can recover without restarting gameplay. Only selected loops own render slots;
pooled flame/light resources are reused and released on cleanup. Proximity bands and
rotating leases share admission between competing loops. Logical metadata is proportional
to live owners; the particle and render resources are explicitly capped.
Defaults are 1,500 particles per frame, 64 selected loops, 48 flame slots and 32 loop
light slots. Loop selection sorts eligible requests (O(n log n)); these bounds cap
render resources, not the cost of arbitrarily many live gameplay owners. Whole-world
retirement clears pending layers, decals and callbacks before identities can be reused.

Content coverage includes existing compound/defense placement sounds, actual gate
open/close transitions and large-furnace fire/audio roles. Stationary fitted windows do
not invent opening actions to satisfy a generic door-model test. Physical surface
identity does not imply a distinct authored recording for every material.

Regression coverage includes physical target/material/normal propagation, authoritative
terminal identity, response/reference coverage, data-only surface extension, foot contact
and footwear, action cue cancellation/late observers, and accepted intent feedback.
Pure budget tests exercise ranking and limits; opt-in native checks exercise real
Godot players, loop recovery, particle/resource limits and moving decals:

```text
--audio-loop-check
--fx-budget-check <output.json> [--shot <output.png>]
--contact-feedback-check <output.json> [--shot <output.png>]
```

Pass these options after Godot's `--` separator. The audio check supports `--headless`.
Run rendered checks with `--rendering-method forward_plus` before that separator;
the compatibility renderer does not support the GPU particle emission path under test.
Rigid leaf attachments follow rendered door/gate transforms. The skinned garage
curtain explicitly suppresses rigid attached marks until deforming surface attachments
are supported; assigning it a rigid frame would leave marks floating during opening.

Checks use isolated fixtures without game saves or a server. Their measured fixture
costs are not an integrated multiplayer performance guarantee. Catalogue validation,
runtime lifecycle checks, visual inspection and listening approval remain separate.
