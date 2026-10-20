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
   Nothing refuses them, stops them or reverses them. (A second press of a door's own verb while it moves turns it round
   from where it is: that is *proposed* here, not specified by the owner, §4.)
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

**A known cost.** Both faces alike means the back face is a copy of the front's relief, so each leaf has about twice the triangles,
and the kit GLBs are bigger: `doors_metal.glb` 3.3 → 8.0 MB, `doors_wood.glb` 2.8 → 6.3 MB, `doors_armored.glb` 2.7 → 6.3 MB (the
files the placed doors are drawn from). A lighter back (the front's decimated copy) is an
option if the cost shows in a frame or a download; it has not been measured to.

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
enter). **This default is unconfirmed**: two secondary Rust guides (falconrust.com/guides/building/doors-and-locks among
them) say a door opens *toward* whoever placed it, which Facepunch has not confirmed; the owner, who plays Rust, decides (§10). **R** steps through the four layouts of a single door in the order left/in, right/in, right/out, left/out (each
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
starts the motion from the progress at that tick. A second press during a move turns it round from where it is, with no refusal:
**a proposal** (the owner's rule is that nothing refuses or stops a door; whether a second press may reverse one is not specified). The server
rests a door at its end (progress 0 or 1, still) and does not announce it: the record already says so.

The pose of a leaf is a function of its progress alone (§5), so the server, every client and a test compute the same pose at the
same tick. The record is what is replicated (§8); a door saved mid-move is saved as finished (its target).

## 5. Collision follows the motion

- **Each tick** the building's `DoorMotionSystem` (under the physics gate, right after the characters moved, before the systems
  that follow what rides on a leaf) puts every moving door's leaf bodies at the pose of the tick's progress: a swing is the
  leaf turned about its pin by `openYaw × ease(progress)`; the garage's body is **only the part of the curtain still hanging**:
  a box from the kit's clip's lower-edge height at that progress (`building.json` `lift`: 25 heights at equal steps of
  progress, 0 to 2.43 m, measured on the clip in Blender) up to the header, 2.85 m minus that, so it shrinks as the curtain
  rolls and is 0.42 m deep at the top when open. It is never the curtain's full size lifted: that box reached 2.4 m into the
  storey above and blocked it (the placement's rays, shots and walkers upstairs). Its height is rounded up to a centimetre,
  so the shapes are shared (`LeafDef.HangingCentimetres`) and the body is re-shaped by `BuildingPhysics.Reshape`. The body
  the targeting rays, blasts and reach use is the same box (`BuildingWorld.LeafParts`). `DoorTests`: through the opening's plane,
  3.2 to 5.6 m up, the way is clear open, shut and every 7 ticks between. A door at rest keeps its bodies
  where they are; an open leaf is solid. At most two bodies a door, moved with Jolt's own call (the broadphase is
  rebuilt once a tick while any door moves: 0.1–0.3 ms).
- **Order.** In one tick the characters move first, then the systems; so the characters' movement in tick *t + 1* meets the poses of
  tick *t*. A client's prediction poses its copies of the bodies at progress(*t* − 1) before it predicts tick *t*
  (`Predictor.BeforeStep`, every tick of a replay too: `BuildingWorld.StepDoors`), and draws a door at the same tick
  (`PredictedTickClock − 1`), so what a player walks into is what they see.
- **A character in the way is shoved.** The character controller's penetration recovery pushes a capsule out of a body that has
  moved into it; nothing stops the door. What it does to a player in a closing leaf's way was found by running it
  (`DoorTests`, a standing player 0.45 m inside a door that swings in, hinged on the left): every tick she is on the leaf's
  leading side, the face it is moving toward, and never on its far side or inside its core; the leaf sweeps her along its arc,
  and when it has shut she stands clear of it in front of that face. For that case this is z +0.38 on the doorway's outside, so
  a door shutting from the inside moved her out through the doorway. The test asserts the invariant (never on the far side, never
  inside the shut leaf), not a side; a player whom the penetration recovery cannot move (against a wall, between two leaves) is
  not tested. A walker meeting a garage curtain is stopped while its lower edge is lower than they are tall (standing 1.76 m,
  crouching 1.10 m: `DoorTests`, a walker at a curtain with its edge at about 0.9 m); a curtain coming down on a standing player
  is not tested.
- **No refusals.** `Toggle` has no `SwingBlocked`, fitting a door no `PlayerInTheWay`; locks and keys are the only things that
  refuse a door (`Locked`), as before.
- **Nor for sight, where the door shows.** The interaction's one line-of-sight ray (GROUND_SYSTEMS.md §3.4) used to go to the door's
  *nearest* point, which for an open leaf lies behind its own jamb as seen from anywhere but the doorway's axis: an inward-swinging
  door could be closed from 3.3 m only from almost straight in front, and from the side the answer was "Something is in the way"
  (the owner's report, 20 October). A door is now seen when the eye sees any of its leaves' points (three across, two up, where
  the leaf is now) or its doorway (three across, two up, in the wall's middle plane): `ITargetFamily.SightPoints`,
  `DoorShapes.SightPoints`, `AimShapes.DoorwaySightPoints`. What still refuses is a real wall: from behind the hut's side wall
  the door is not seen. `DoorTests.ADoorIsNeverRefusedForSightWhereItsDoorwayShowsAndStillNotThroughAWall` (every layout, open and
  shut; the answer is Done or OutOfReach, never OutOfSight; before the fix, with the door open, 76 of the 132 sample points of the ring refused at least one of its two targets, the leaf or the opening) and
  `AGarageDoorIsNeverRefusedForSightFromWhereItsOpeningShows`. The reach itself (3.5 m to the nearest surface) is unchanged.

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
tuning's radius and height), by walking a character through the real movement code, and by sampling whether two open leaves
pass through one another (doors ignore each other, so leaves that cross would be drawn through each other and are no airlock):

1. **The owner's airlock: a triangle foundation with two single doors, both swinging into the room.** The owner's words: the
   outside door opens toward the inside and the inside door opens toward the outside, so that with both open nobody gets in.
   The inside door "opens toward the outside" as the base's inside sees it: into the airlock, so both open leaves end up in the
   small room, one hung against a wall and the other across the walk. Any two edges of a triangle meet at a corner, 60° inside;
   seen from outside, one door's right and the other's left point to it. For two doors that both swing in (layouts 0 to 1,
   `DoorTests.AnAirlockWithBothDoorsSwingingIntoTheRoomClosesTheWalkWithoutTheLeavesCrossing`):

   | First, second | The walk | The open leaves |
   |---|---|---|
   | **0, 0** | closed: the first hangs from the far side and lies against the third wall, the second hangs at the corner and stands across the walk | clear one another |
   | **1, 1** | closed: the mirror of 0, 0: the first stands across, the second lies against its wall | clear one another |
   | 1, 0 | closed | **cross** (both hang at the corner): not usable |
   | 0, 1 | free: both lie against the walls | clear one another |

   Use **0, 0** (or its mirror 1, 1). Pictures: `images/abtest-b/doors/airlocks-sandbox/triangle-both-in-*` and the hosted
   `airlock-triangle-both-in-*`. (The layouts above follow the owner's description; the owner's own pictures were not seen here.)
2. **The same triangle with the outer door swinging in and the inner one out** is an airlock too, from the outer door alone: the
   first door with layout 1 (hung at the corner, swinging in) or the second with layout 0 closes the walk; with the second door
   swinging out (layout 2) its leaf is outside the room and the outer leaf does all the blocking. Hung the other way (layouts 0
   and 3: hinged at the far sides, one swinging out) the leaves lie against the walls and the walk is free. Kept as an extra layout.
3. **A double door on one wall and a single door on the next** in a square room: the double door swinging in (layout 0) with the
   single door hinged on the right, in or out (layout 1 or 3), leaves the way free with either door open alone and closes it with
   both open, the open leaves clear of one another. Hinged on the left (0 or 2), or with the double door swinging out, the way is free.

Pictures of each, open and shut: `images/abtest-b/doors/airlocks-sandbox` (`--view build --scene airlocks --tour`), and the
hosted set `images/abtest-b/doors/hosted`.

## 8. Replication and saving

- `DoorNet` carries the **layout** (replacing the flip flag; 2 bits, since protocol 44, D2), whether it is open (its target),
  and the motion record (protocol 45, D3): *from* (progress as a fraction of 65,535), *since* (the tick, a varint) and *moving* (two
  bits: 0 still, 1 opening, 2 shutting). A door that is still has `moving = 0`. The numbers 44 and 45 are this branch's (abtest-b was
  at 43 when it was cut): a landing re-checks them against the version abtest-b has by then, as other sessions land too.
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
| D4 (done) | The four door items' icons and dropped models from the corrected kit doors (docs/ITEM_ART_SALVAGE.md's pattern; docs/ITEM_ART_DOORS.md) | the item pictures; `models.json` lost their stand-in notes; `ContentTests`, `Model`, `Icon`, `Gib` |

The placement ghost's four layouts of a single door (R's steps; leaves part open, the swing arc on the floor): `images/abtest-b/doors/ghosts-wooden-door.jpg`. Hosted pictures of the two airlock layouts, open and shut, from outside and inside, close D1 to D3. Owner visual
acceptance is pending.

## 10. Open points (not decided here)

- **Which way a new door opens.** The default layout opens away from the viewer (§3); two secondary Rust guides say toward
  whoever placed it. Unconfirmed; the owner decides.
- **A second press while a door moves** reverses it from where it is (§4); the owner has not specified it.

- **Which face carries the keypad** is the frame's +Z (§6). Choosing it by the side the player aimed from would need the
  face stored with the lock (its record and its message).
- **Sizes against Rust's.** The module (3 m foundation and storey, 0.3 m wall) and the openings (single 1.2 × 2.3 m,
  double and garage 2.7 × 2.85 m) are the PSG kits' own contract (`dimension_contract.json`), taken as RustMVP's;
  REPAIR_BUILDING.md already says the 3 m footing depth is a local tuning value, not a claimed Rust constant. They have not
  been measured against Rust's here; a size audit is a separate package.
- **The fitted double doors' items** (`items/fitted/wooden-double-door.glb`, `armoured-double-door.glb`) are still cut from the kit before D1
  (asymmetric leaves); the sheet metal one was redrawn with D4. A separate small package.
