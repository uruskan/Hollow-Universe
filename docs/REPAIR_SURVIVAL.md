# Survival and animal repair evidence

## Immunity and death

The direct death path bypassed the damage path's god-mode check. Ship wrecks could
therefore kill an immune occupant. `PlayerSystem.Die` now checks immunity before
moving inventory, resetting state, or creating a corpse. Enabling immunity also
recovers an already wounded player. Wreck handling still releases occupants from
the destroyed ship and its coordinate frame.

Engine-free tests cover direct death, ordinary damage, wounded recovery, disabling
immunity, and an immune Iron Wake pilot continuing for 90 ticks after destruction.
These prevent the reported invalid death route. There is no native crash stack
from the original report, so they do not establish that every possible death-time
engine crash has been diagnosed.

## Breathing and immersion

One shared breathing rule samples the actual eye position against
`ISurface.WaterLevelAt`, including inland lakes and streams. The default reserve is
20 seconds; air restores five seconds of reserve per second. An exhausted reserve
deals 10 health per second, bypasses the wounded state, and respects immunity.
Sleeping and wounded players still breathe. Reserve survives reconnects in player
save version 6; older saves start with a full reserve. The server replicates reserve
and suffocation state for the oxygen HUD. The camera's separate underwater effect
follows its own position, including orbit views.

Swimming now uses the same local water query rather than only a global ocean
radius. Tests cover reserve exhaustion/recovery, old/new save decoding, actual
drowning, immunity, and inland water on four planetary axes. Oxygen equipment
remains a design requirement; this repair does not invent or claim that equipment.

## Animals

Animal simulation headings are relative to planetary north. Their renderer used
that raw heading against a different projected axis. Rendering now converts the
simulation's forward vector into the character renderer's frame, preserving the
animal's radial up and actual movement direction.

Attack feasibility is checked both when choosing an attack and when applying its
damage. A ground animal cannot bite through a blocking structure, attack a player
in a ship frame, or attack a player supported by a constructed floor/foundation.
The foundation regression verifies that an attempted bite causes no damage.
The existing animal regression suite covers herd movement, warnings/chases,
attacks, harvest, spawning and network state; it passed after these changes.

Reference research: [Facepunch devblog 188](https://rust.facepunch.com/news/devblog-188)
describes preventing animal attacks through structures. Rust's later wolf behavior
has changed; we implement the owner's requested foundation protection rather than
claiming an exact copy of every current Rust animal rule.

## Validation

- `.artifacts/repair-focused2.log`: 20/20 focused checks passed, including six
  survival repairs, seven ship repairs, admin input, bow release and catalogue.
- `.artifacts/repair-regression2.log`: 26/26 passed, including animal behavior and
  network coverage, swimming, wounded state and audio foot contact.
- Runtime visual evidence is recorded separately; passing simulation tests alone
  is not a claim that animal animation, water or character poses look finished.
