# Doors: how they behave (specification, 20 October 2026)

The owner's specification of how our doors behave, like Rust's. It replaces what GROUND.md §6.3 said about doors before
(a leaf turned 180° swapped the hinge side and the swing together, a door refused to move into a player, bodies jumped
to the open pose at once). Where the code still differs, the work packages D1 to D4 below close the gap; DEVELOPMENT.md
records each with its honest validation. Rust's own pages agree on the points that matter: a door opens one fixed way,
shown by the placement guide; an *airlock* is "2 or more opposing doors within a single foundation" where the open
inner door blocks access (wiki.facepunch.com/rust/Building-terminology); doors are animated by the server.

## 1. The rules

1. **Both faces of a door look the same**, inside and outside. A door has no "good" side.
2. **Doors ignore everything in their way**: other doors, players, objects. They always open and they always close.
   Nothing refuses them, stops them or reverses them (the only thing that reverses a door is a second press of its
   own verb while it moves, §4).
3. **Collision follows the real motion.** A swinging leaf's collision moves along its swing, a garage curtain's along
   its roll, every tick. There is no tick at which the body is somewhere the picture is not: nobody can walk under a
   closing garage curtain because its body had already jumped up.
4. **Airlocks come from where the open leaves end up.** The door's hinge side and swing side are chosen separately,
   so a builder can place two doors whose open leaves block the walkway between them (§7).
5. **A code lock is seen from both sides** of its door (§6).

## 2. The parts

| Part | What | Frame of reference |
|---|---|---|
| Frame insert | The kit's lining of the opening (jambs, head, sill), part of the door item | The opening's own frame: origin at the wall's centre line (`z = 0` is the middle of the wall's thickness), +Y up, +Z the side the piece calls outside |
| Leaf | One swinging panel; a single door has one, a double door two (a left and a right) | Its origin is its hinge pin, on the leaf's mid-plane. The leaf extends in +X from the pin (a left-hinged leaf) or −X (a right-hinged one) |
| Curtain | The garage door's one rolling, skinned panel | Its origin the bottom-left corner of the closed curtain |

**The contract the art keeps** (`art/import/door_kit.py`): a leaf is mirror-symmetric about its mid-plane (the kit's
detailed front face is copied onto the back), its hinge pin lies on that plane at the frame's `z = 0`, and the frame
insert is centred in the wall (`z` from −0.147 to +0.147 in the 0.3 m wall). **A door lies wholly inside its frame**, as
Rust's do: the leaf is 0.8 as deep as the kit's, so its planks end 6 cm and its handle plate 2.7 cm short of the frame's
faces (leaf `z` ±0.12 at the plate, ±0.04 for its core, which is its collision). The kit as delivered had a relief face
and a flat face, a lever on the flat one, a leaf mounted proud of its frame and a frame 3 cm off centre (the owner saw the
door standing out of its frame). A mirrored right-hinged copy of every single leaf is made at the import; double doors keep
the kit's own left and right leaves, made symmetric the same way. The garage door's art is the kit's own, unchanged: its
two faces are slatted alike, its housing is wider than the wall on purpose.

## 3. Layouts: hinge side and swing side

A **layout** says on which side of the opening the hinge is and which way the leaves swing. It is one small number
stored with the door (`Door.Layout`, replacing `Flip`):

| Bit | 0 | 1 |
|---|---|---|
| 0, hinge | left (as seen from +Z, the leaf extends toward +X from a pin at −X) | right |
| 1, swing | *in*: the open leaf lies on the −Z side of the wall | *out*: on the +Z side |

- A **single door** has all four layouts (hinge left or right × opens in or out).
- A **double door** has two (opens in or out); each leaf hinges at its own jamb. The hinge bit is not used.
- The **garage door** has one; it rolls into its header and has no swing.

The **hinge pin** is on the face of the leaf's core the leaf swings toward (0.04 m from its middle plane, on the −Z
face for *in*, +Z for *out*), so the open leaf stands clear of the jamb it hangs on. The open pose is the leaf turned about its pin
by the leaf's `openYaw` (100°, from the kit's assembly manifest), negated for a right-hinged leaf and for the *out* swing. So layout 0 is today's single door, hung on the left and
opening toward −Z.

**Placing.** The placement ghost is the door itself, drawn partly open, with the **swing arc** the leaf sweeps drawn on
the floor of the opening (a fan from the shut pose to the open one, about the hinge pin). Aiming through an opening from one side
starts the layout hinged on the viewer's left and opening *away from the viewer* (into the room the viewer is about to
enter). **R** steps through the four layouts of a single door in the order left/in, right/in, right/out, left/out (each
press changes one of the two choices); for a double door it swaps in and out. The chosen number travels with the
placement claim and is checked against the door's layout count. The ghost is refused only for the placement's own
reasons (no opening aimed at, a door in that frame already, out of reach). A door is never refused, at placing or
afterwards, because a player, another door or an object is in the way.

## 4. Motion

A door has a **progress** from 0 (shut) to 1 (open) and a motion record, as the Iron Wake's doors do (IRON_WAKE.md
§5.3): the progress *from* at a tick *since*, and whether it is *moving* open (+1), moving shut (−1) or still (0), besides
its *target* (`Door.Open`: where it is going). Where it is at any tick is arithmetic, the same on every machine:
`from + moving × (tick − since) × rate`, clamped to 0..1, with `rate = 1 / (travel × 30)` per tick. The travel comes from
data (building.json `travel`): a leaf swings in **1.0 s** (30 ticks, easing at both ends: `DoorDef.Ease`), the garage curtain
rolls in **3.0 s** (90 ticks: the kit's Open clip, frames 2 to 92 at 30 per second, measured in Blender). A press of the verb
starts the motion from the progress at that tick; a second press during a move reverses it from where it is, with no refusal. The server
rests a door at its end (progress 0 or 1, still) and does not announce it: the record already says so.

The pose of a leaf is a function of its progress alone (§5), so the server, every client and a test compute the same pose at the
same tick. The record is what is replicated (§8); a door saved mid-move is saved as finished (its target).

## 5. Collision follows the motion

- **Each tick** the building's `DoorMotionSystem` (under the physics gate, right after the characters moved, before the systems
  that follow what rides on a leaf) puts every moving door's leaf bodies at the pose of the tick's progress: a swing is the
  leaf turned about its pin by `openYaw × ease(progress)`; the garage's body is a box of the curtain's size lifted by the
  kit's clip's lower-edge height at that progress (`building.json` `lift`: 25 heights at equal steps of progress, 0 to 2.43 m,
  measured on the clip in Blender), so the body is where the skinned curtain's lower edge is. A door at rest keeps its bodies
  where they are; an open leaf is solid. At most two bodies a door, moved with Jolt's own call (the broadphase is
  rebuilt once a tick while any door moves: 0.1–0.3 ms).
- **Order.** In one tick the characters move first, then the systems; so the characters' movement in tick *t + 1* meets the poses of
  tick *t*. A client's prediction poses its copies of the bodies at progress(*t* − 1) before it predicts tick *t*
  (`Predictor.BeforeStep`, every tick of a replay too: `BuildingWorld.StepDoors`), and draws a door at the same tick
  (`PredictedTickClock − 1`), so what a player walks into is what they see.
- **A character in the way is shoved.** The character controller's penetration recovery pushes a capsule out of a body that has
  moved into it; nothing stops the door. A door that closes on a standing player ends up with them outside its leaf: the leaf
  sweeps them ahead of it, and near its end it pushes toward the doorway, so a door closing from the inside sees them out
  through it (`DoorTests`: z +0.38 outside the doorway). A garage curtain coming down presses a standing player down and aside; nobody
  can *walk* under it while its lower edge is lower than they are tall (standing 1.76 m, crouching 1.10 m): a walker at a curtain
  with its edge at about 0.9 m is stopped.
- **No refusals.** `Toggle` has no `SwingBlocked`, fitting a door no `PlayerInTheWay`; locks and keys are the only things that
  refuse a door (`Locked`), as before.

## 6. Locks on both faces

A door's lock is mounted on its **first leaf** by its **free edge**, at hand height, on the handle plate the kit puts
there (the same plate on both faces: the kit's handle, copied onto the back with the rest of the face). Two sockets, one
on each face of the leaf, at the same place seen from either side (`LeafDef.lockDepth`, 0.12 m from the leaf's middle plane):

- the **keypad** (a code lock) or the **key cylinder** (a key lock), the lock item's own model, on the +Z face (the
  outside of the frame piece);
- a **back plate**, the same model pressed flat to a housing (0.4 of its depth), turned round, on the −Z face, so a
  door with a lock shows one from both sides.

Which face carries the keypad is the frame's, not the aim's: choosing it by the side the player aimed from would need the
face stored with the lock (its record, its message), and is not part of this specification (§10).

Interacting works from both faces already (the door's leaf is the target; reach and sight are the building's, not a
face's). A lock is still one per door and swings with the leaf; the lock's spot is computed from the leaf's current
pose (`Door.LockSpot`).

## 7. Airlocks

An airlock is two doors the builder places so that, with both open, the walkway between them is blocked. Our rule needs
nothing special for it: leaves are solid whether open or shut, and the layouts (§3) let a builder choose where the open leaf
ends up. `DoorTests` finds which layouts close which walks by flooding the floor with the character's capsule (the movement
tuning's radius and height) and, for the two cases below, by walking a character through the real movement code:

1. **A triangle foundation with two single doors** on two of its edges (any two meet at a corner, 60° inside). Seen from
   outside, one door's right and the other's left point to that shared corner. A door **hinged on the corner's side and
   swinging into the room** puts its leaf across the walk to the other doorway: the first door (cyclically) with layout 1 or the
   second with layout 0. With the first door at layout 1 and the second swinging out (layout 2) both open, nobody passes (the
   outer door opens in, the inner opens out); hung the other way (layouts 0 and 3: hinged at the far sides, one swinging out)
   the leaves lie against the walls and the walk is free. In so small a room one inward leaf is enough; the second door
   only has to open away.
2. **A double door on one wall and a single door on the next** in a square room: the double door swinging in (layout 0) with the
   single door hinged on the right, in or out (layout 1 or 3), leaves the way free with either door open alone and closes it with
   both open. Hinged on the left (0 or 2), or with the double door swinging out, the way is free.

Pictures of both, open and shut: `images/abtest-b/doors/airlocks-sandbox` (`--view build --scene airlocks --tour`), and the hosted
set `images/abtest-b/doors/hosted`.

## 8. Replication and saving

- `DoorNet` carries the **layout** (replacing the flip flag; 2 bits, since protocol 44, D2), whether it is open (its target),
  and the motion record (protocol 45, D3): *from* (progress as a fraction of 65,535), *since* (the tick, a varint) and *moving* (two
  bits: 0 still, 1 opening, 2 shutting). A door that is still has `moving = 0`.
- A client's copy of a door animates from the record against the client's own tick (the one its prediction runs on, less
  one), so the picture and the predicted collision are the same pose. Its first sight of a moving door is a little ahead
  of the server's pose by the lead of its clock; the press itself is not predicted (it is answered by the server's next door
  message).
- The save stores the layout in the door's row (door version 2; a version-1 row's half turn reads as layout 3) and the door open
  or shut as it finishes. The meaning of the stored number changed, so the Earth save revision went up with it (10; the default
  hosted save is `hollow-server-earth-v10.db`).

## 9. Packages

| | What | Checked by |
|---|---|---|
| D0 | This document | — |
| D1 (done) | Symmetric leaves, centred frame, both-face locks | `images/abtest-b/doors/before` and `after` (`doors.jpg`: every door from outside and inside; `locks.jpg`: the lock seat on both faces) |
| D2 (done) | Hinge and swing chosen separately, the ghost's arc, the airlock layouts | `DoorTests` (five): every layout's pose, pin and body; R's steps and default; the two airlocks by flood and a character walking |
| D3 (done) | No refusals; bodies follow the motion every tick; the motion record on the wire (protocol 45) | `DoorTests` (eight in all): the travel time and the body's ray test every tick, closing on a player, fitting and shutting with a player in the way, the garage curtain's edge and walkers; `BuildingNetTests`: the server's and the client's leaf agree at every tick |
| D4 | The four door items' icons and dropped models from the corrected kit doors (docs/ITEM_ART_SALVAGE.md's pattern) | the item pictures; `models.json` loses their stand-in notes |

Hosted pictures of the two airlock layouts, open and shut, from outside and inside, close D1 to D3. Owner visual
acceptance is pending after D4.

## 10. Open points (not decided here)

- **Which face carries the keypad** is the frame's +Z (§6). Choosing it by the side the player aimed from would need the
  face stored with the lock (its record and its message).
- **Sizes against Rust's.** The module (3 m foundation and storey, 0.3 m wall) and the openings (single 1.2 × 2.3 m,
  double and garage 2.7 × 2.85 m) are the PSG kits' own contract (`dimension_contract.json`), taken as RustMVP's;
  REPAIR_BUILDING.md already says the 3 m footing depth is a local tuning value, not a claimed Rust constant. They have not
  been measured against Rust's here; a size audit is a separate package.
