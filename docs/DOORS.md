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

The open pose is the leaf turned about its pin by the leaf's `openYaw` (100°, from the kit's assembly manifest),
negated for a right-hinged leaf and for the *out* swing. So layout 0 is today's single door, hung on the left and
opening toward −Z.

**Placing.** The placement ghost is the door itself, drawn partly open, with the **swing arc** the leaf sweeps drawn on
the floor of the opening (a thin wedge from the shut pose to the open one). Aiming through an opening from one side
starts the layout hinged on the viewer's left and opening *away from the viewer* (into the room the viewer is about to
enter). **R** steps through the four layouts of a single door in the order left/in, right/in, right/out, left/out (each
press changes one of the two choices); for a double door it swaps in and out. The chosen number travels with the
placement claim and is checked against the door's layout count. The ghost is refused only for the placement's own
reasons (no opening aimed at, a door in that frame already, out of reach). A door is never refused, at placing or
afterwards, because a player, another door or an object is in the way.

## 4. Motion

A door has a **progress** from 0 (shut) to 1 (open) and a motion record, as the Iron Wake's doors do (IRON_WAKE.md
§5.3): the progress *from* at a tick *since*, and whether it is *moving* open, moving shut or still. Where it is at any
tick is arithmetic: `from + moving × (tick − since) × rate`, clamped to 0..1, the same on every machine. The rate comes
from data: a leaf swings in **1.0 s** (30 ticks, easing at both ends), the garage curtain rolls in **3.07 s** (the
kit's clip). A press of the verb sets `from` to the progress now, `since` to the tick, and the direction: pressing
during a move reverses it from where it is, with no refusal. A door left to finish ends still at 0 or 1.

The pose of a leaf is a function of its progress alone (§5), so the server, every client and a test compute the same
pose at the same tick. The record is what is replicated (§8) and saved (a door saved mid-move is saved as finished).

## 5. Collision follows the motion

- Every tick the server puts each moving door's leaf bodies at the pose of the tick's progress (a swing: the leaf
  turned about its pin by `openYaw × ease(progress)`; the garage: the curtain's body, a box of the curtain's size, lifted
  so its lower edge is where the skinned curtain's lower edge is, taken from the kit's clip and stored in building.json).
  A door at rest keeps its bodies where they are; an open leaf is solid.
- **Order.** In one tick the characters move first, then the systems; a door's bodies are put at progress(tick) in the
  building system's step, so the characters' movement in tick *t + 1* meets the poses of tick *t*. A client's prediction
  poses its copies of the bodies at progress(*t* − 1) before it predicts tick *t* (including every tick of a replay).
- **A character in the way is shoved.** The character controller's penetration recovery pushes a capsule out of a
  body that has moved into it; nothing stops the door. A door that closes on a standing player ends up with them outside
  its leaf (on the side the leaf came from, since it sweeps them ahead of it). A garage curtain comes down on a player
  standing under it, who is pressed down and aside by the controller; nobody can *walk* under it while its lower edge is
  lower than they are tall (standing 1.8 m, crouching 1.0 m).
- A door that finishes (progress 0 or 1) moves its bodies once more, to the exact pose of the end.

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

An airlock is two doors the builder places so that, with both open, the walkway between them is blocked. Our rule
needs nothing special for it: leaves are solid whether open or shut, and the layouts (§3) let a builder choose where the
open leaf ends up. Two layouts that are meant to work, to be built by the tests of D2 and checked by a character walking
(no layout is special in the code):

1. **A triangle foundation with two single doors.** Two of the triangle's three walls are doorways. The door in the
   outer wall opens in (its leaf lies inside the triangle), the door in the inner wall opens out (its leaf also lies
   inside the triangle). Hinged on the sides the tests record, the two open leaves close the walk from one doorway to the
   other even with both doors open.
2. **A double door on one wall and a single door opening inward on another**, in one foundation: the double door's
   open leaves and the single leaf together fill the room between them.

D2 records the exact hinge sides here once the movement test has found them.

## 8. Replication and saving

- `DoorNet` carries the **layout** (replacing the flip flag), whether it is open, and the motion record: *from*
  (progress as a fraction of 65,535), *since* (the tick, 32 bits) and *moving* (two bits). A door that is still has
  `moving = 0`. The protocol number goes up (44), because the meaning of a door message changes.
- A client's copy of a door animates from the record against the client's own tick (the one its prediction runs on), so
  the picture and the predicted collision are the same pose. Its first sight of a moving door is a little ahead of the
  server's pose by the lead of its clock; no client prediction of the press itself (a press is answered by the server's
  next door message).
- The save stores the layout in the door's row (door version 2) and the door open or shut as it finishes. The meaning
  of the stored number changed, so the Earth save revision goes up with this change (a new hosted save).

## 9. Packages

| | What | Checked by |
|---|---|---|
| D0 | This document | — |
| D1 (done) | Symmetric leaves, centred frame, both-face locks | `images/abtest-b/doors/before` and `after` (`doors.jpg`: every door from outside and inside; `locks.jpg`: the lock seat on both faces) |
| D2 | Hinge and swing chosen separately, the ghost's arc, the airlock layouts | `DoorTests`: every layout's open pose; the two airlocks built, a character cannot pass with both doors open |
| D3 | No refusals; bodies follow the motion every tick; the motion record on the wire; the protocol and save revision | `DoorTests`: closing on a player, the garage curtain at mid-close, server and client leaf poses agree mid-swing |
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
