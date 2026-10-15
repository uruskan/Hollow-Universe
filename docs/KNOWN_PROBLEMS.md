# Known problems and the plan for them (14 October 2026)

One place for everything that is known to be wrong or missing in the ground game, where it was found, what causes it,
which package fixes it, and whether that package has started. Before this file the list was spread over the owner's
playtest chats, the handoff, the design doc's "left for later" notes and the session memory, and the owner was not
aware of most of it. The rule from now on: **every playtest finding goes here with its cause and its package, the day
it is found; a fixed one gets its date and commit.** The handoff points here; AGENTS.md lists it among the docs to
read first.

Status words: *open* (nothing done), *planned* (in a package below, not started), *designing* (a design doc is being
written or waits for the owner), *building* (an approved package is being built), *waiting* (needs the owner's word),
*done* (with the date).

## 1. The second playtest (14 October 2026, about 3 am): 30 findings, none fixed yet

The owner played the whole build and wrote 30 findings. Six read-only agents traced each to its cause in the code the
same night, and a plan of three waves was proposed (§4). **The plan was never approved, so nothing of it was built.**
The same day went to the kernel (GROUND_SYSTEMS.md §12), the merges into `main`, the scale proposal (SCALE.md) and the
F2 developer panel (GROUND.md §8.5), none of which touch these findings. Numbers are the owner's.

### The world and the map (findings 1 and 3): now the scale proposal

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 1 | The map does not show the whole world; it is flat on a sphere. | The map covers a 3.9 km square, 6 % of the 16 km zone, and opens zoomed to 1.5 km; flat is right at zone scale (2 ppm distortion). | Scale, package S3: one zone holds the whole 2.4 km world, and one map shows it as a globe that becomes a flat chart as you zoom in (WHOLE_WORLD.md §11). The owner asked again on 14 October 2026, evening, said go for S3 and approved its design. | building (S3 merged into `main` 15 October: S3a, S3b; the map is S3d) |
| 3 | 30 to 80 fps, thick forest everywhere, no biomes, valleys, rivers, lakes, cliffs or flat land; the 4K scans unused. | Everything that varies the land changes over 60 to 2,200 km, so a 16 km zone gets one landscape; no code for rivers, lakes, cliffs or plateaus; 8 of 34 scan sets reach the game, none of the 62 scanned cliffs and rocks. | World v3 was to be a design first; the owner's scale decisions of 14 October (small worlds, landmarks) replace it: SCALE.md carries the world's features at walking scale. | waiting (SCALE.md) |

### Performance and hit feedback (finding 23, plus the frame rate)

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 23 | Chopping a tree: the swing and the hit on the tree go out of step; the frame stutters. | Every axe hit rebuilds and re-uploads all the plants around the player on the main thread (20 to 30 ms a swing, and every 8 m walked); the chips and the sound wait for the server (70 to 150 ms after the visible contact, even hosting; GROUND.md H12: "predicting one's own hits is left for later"); the arms swing every 0.855 s and the server every 0.867 s, so they drift apart. | Performance and hit feedback: a budget for main-thread uploads; one's own hits predicted at the moment of contact and reconciled with the server; the swing clocks aligned. | planned (wave 1) |
| – | 30 to 80 fps in the forest. | The moon's light draws the forest's shadows a second time in daytime whenever the moon is up; the forest density itself (300 trees a hectare). | The same package for the moon; the density is the world's (SCALE.md). | planned (wave 1) |
| 19 | Hit and headshot sounds are hard to hear. | They are 3 dB quieter than the shot and arrive a server round trip late, buried under the gunshot. | Performance and hit feedback. | planned (wave 1) |
| 24 | The hit marker does not say what was hit. | The server knows exactly what was hit but sends only its kind. | Performance and hit feedback. | planned (wave 1) |

### Ships and keys (findings 28 and 29)

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 28 | Q and E open menus while flying; a seat should be left with Space. | The interface's keys do not know the player is seated: Q opens crafting (Q is also roll), any open screen zeroes every flight axis, so one press of Q and the ship cannot be flown; Space is the ship's up; only F leaves the seat, and only at rest; nothing on screen lists the keys. Three handlers hard-code keys and the pilot's keys exist in two copies. | Seats, controls and damage: keys get a state context (on foot, seated, in a menu); Space leaves any seat; the keys shown. | planned (wave 1) |
| 29 | Noclip is too slow. | Fixed at 10 m/s, 40 m/s with Shift. | Seats, controls and damage. | planned (wave 1) |

### Building (findings 2, 4 to 11, 14)

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 2 | Foundations cannot be placed on uneven ground. | The ground under a foundation may differ by only 0.91 m, because every foundation model is exactly 1 m deep (RustMVP's number). Needs a deeper footing model and the owner's choice of depth (3 m proposed). | Building. | planned (wave 1) |
| 4 | The first foundation lands left or right of where the player looks. | Its rotation snaps to a fixed 15° world grid, and near a building it jumps to any socket within 1.75 m, even an occupied one. | Building. | planned (wave 1) |
| 5 | A triangle floor fell when a half wall was destroyed, although two walls touched it. | A floor is held only by the piece whose socket it took; walls it touches do not count, though the contact is already detected. The owner's answer: a floor is held by every wall it rests against. | Building. | planned (wave 1) |
| 6, 7 | Floors and window walls glitch where they meet. | The stone floor, window and doorway meshes have faces drawn twice, on top of each other, since the kit was imported; every grade's window openings and the metal wall frame too. The import never checks a mesh for duplicate faces. | Building (the kit re-imported with a duplicate-face check). | planned (wave 1) |
| 8 | A door's opening direction cannot be seen while placing it. | The door's preview is drawn closed in one flat colour, with no handle, hinge or swing shown. | Building. | planned (wave 1) |
| 9 | A code lock cannot be placed on the middle garage door of three. | Most likely the lock's line-of-sight check treats the garage door's own 8 cm panel as an obstacle; "can't see it" and "something is in the way" share one message. | Building. | planned (wave 1) |
| 10, 14 | Objects need more room than they visibly take; the workbench fits but is refused; no snapping to walls. | Deployables' collision boxes do not match their models (the workbench's is 32 cm wider); touching counts as overlapping; nothing squares an object to a wall. | Building (sizes compared with the art at import). | planned (wave 1) |
| 11 | A placed object flickers for a moment. | The see-through preview stays over the real object for half a second and the two fight for the same pixels. | Building. | planned (wave 1) |

### Interaction and bodies (findings 12, 13, 15, 25, 27)

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 12 | E should name a bed or bag; hold E a radial with Pick up and Give to friend. | Beds and bags only have Pick up, not set as the tap action, so a tap does nothing; naming does not exist anywhere (no name on deployables, on the wire or in the save). | Interaction and bodies. | planned (wave 1) |
| 13 | E on the vehicle workbench does nothing. | The same dead tap: its only verb is Pick up; the Tin Finch is crafted from the normal crafting menu near the bench. | Interaction and bodies. | planned (wave 1) |
| 15 | Bodies end up in the wrong place, fall through floors, cannot be harvested. | The ragdoll most likely starts at the zone's origin before the body is moved into place (unverified); ragdolls collide only with the ground; the body becomes a bag only on the player's own screen. | Interaction and bodies: bodies lie where the player died, are looted and harvested like a carcass, then become a bigger bag. | planned (wave 1) |
| 25 | Radial titles are cut off. | The radial shrinks titles into a fixed width and never wraps them; the cost and reason lines are not fitted at all. | Interaction and bodies. | planned (wave 1) |
| 27 | A ship's fuel tank cannot be opened. | Whatever is opened on a ship is always its cargo; four places assume that. | Interaction and bodies: a one-slot tank opened from the fuel cap. | planned (wave 1) |

### Weapons and gear (findings 16 to 22, 26)

| # | What the owner saw | Cause as traced | Package | Status |
|---|---|---|---|---|
| 16 | The pistol's support hand is bent wrong in first person. | The support pose points the fingers down while the forearm comes up from below, a wrist bend of about 115°; first-person poses are hard-coded per kind and ignore the catalogue's poses. | Weapons and gear (the pose), then P7. | planned (wave 1) |
| 17 | Attachments are not what they should be. | The zoom and the stat changes work; sights never use their own eye point, there is no reticle or scope overlay, the weapon light is never drawn; attaching by dropping and a remove button were an earlier session's shortcut. | Weapons and gear: attachment slots in the gun's panel, one sight, one muzzle, one underbarrel, dragged in and out. | planned (wave 1) |
| 18 | The bow does not draw. | Its draw and release animations exist but never run, because only guns set "aiming". | Weapons and gear. | planned (wave 1) |
| 20 | Armour values are not shown. | Protection per body region exists in the data; the inventory does not show it. | Weapons and gear: a protection panel with the damage types. | planned (wave 1) |
| 21 | Recoil does not feel like Rust. | The numbers are right, but the view kicks and returns by itself (an earlier shortcut); the gun barely moves, no camera shake, no first-person sound layer, crack or casings; sensitivity does not scale with zoom. The owner's direction: old Rust, 2017 to June 2022, learnable patterns, the view moves and stays. | Gunplay feel: a design first (wave 2), then the work. | designing (not started) |
| 22 | Character animation and grips look wrong. | Third-person grips do not fit the hands to each item; first-person poses are code, not data; no proper bow hold. | Characters and animation (P7): a design first (wave 2). | designing (not started) |
| 26 | Rockets: the HV rocket must kill any living thing; the regular one should fly slower and drop more. | Drop is set on the launcher, so both rockets fall alike; the HV rocket does less to living things (175) than the regular one (210), and a bear has 400 health. | Weapons and gear. | planned (wave 1) |

### The architecture (finding 30)

The owner suspected the shared systems were being bypassed. The trace agreed about the edges: harm that is not an
attack (falls, hunger, a wrecked ship) calls `Hurt` and `Die` directly and skips the damage pipeline, so a crash kills
through god mode; keys have no state context; main-thread uploads have no budget; model sizes are never compared with
gameplay sizes; some data is read by nothing (the weapon light, sights' eye points, casing sounds). Each wave 1 package
fixes its bugs and adds a guard so the problem cannot come back: only the damage pipeline may change health, enforced
by the compiler, with one test that god mode survives every source of harm. Status: planned (wave 1).

## 2. Gaps the design doc admits

These are written in GROUND.md as decisions of the form "left for later" and were never in a plan.

| Where | What is missing | Status |
|---|---|---|
| GROUND.md 76, H12 | One's own hit plays a round trip after the swing; predicting one's own hits is left for later. The same as finding 23. | planned (wave 1) |
| GROUND.md H14 | Outcrops are still spheres to walk into and for the swing's thick ray, taller than their broken stages; only the weak spot uses the drawn surface. | open |
| GROUND.md 52 | A broken tool vanishes; Rust keeps broken items for repair. | open |
| GROUND.md §4.9 | No burning sound (nothing in the protocol says a player burns), no sand footsteps. | open |
| GROUND.md 75, 78 | Sleepers stay ragdolls; others' aiming pose, crawling and sleeping poses are not drawn, because the character contract has no flag or stance for them. | P7 (wave 2) |
| GROUND.md §5.11 | Replication is 2 to 3 ms of the tick on one thread; writing each player's snapshot on worker threads is the next step if a zone needs 200 players. The risks of doing it are listed in the handoff of 14 October. | open (not needed for one server yet) |
| handoff | Night bloom on some glowing models; two Jolt crashes under low memory (unconfirmed); the 40 to 60 ms ragdoll hitch; stand-in models (`standIn` flags in `models.json`); sound mixing by ear; cheap-looking effects; Linux server testing. | open |
| handoff | The wardrobe hookup: looks for every remote player and the inventory preview; sleep and crawl stances; aiming for bows; the help-up, eat, bandage and inject events. | P7 (wave 2) |

### Found by reading the code (14 October 2026, the surveys for S3)

| What | Cause | Status |
|---|---|---|
| A herd's members start on one spot, face one way and think on the same tick. | Every animal added on the same tick gets the same seed: `AnimalWorld.AddMember` hashes the counter `_localIds`, which is never incremented. | fixed (S3a; on `main` since the merge of 15 October) |
| Near tree impostors most likely disappear more than about 400 m from the landing site whenever the landing site is out of view (not yet seen in a run). | In play the near set is anchored at the frame's origin (`Vegetation.Game.RebuildNear`), and the near impostors' `CustomAabb` is a fixed ±400 m around that anchor, so Godot culls them by the wrong box. | open, small (on `main`) |
| The map of a surface without a sea is most likely black (not run). | `ZoneMapImage` takes sea level as −∞ when `SeaRadius` is 0, so heights become infinite and the hill shading NaN; only the `--stand-in` world has no sea. | in S3: since S3a the stand-in world has a sea; the map's safe reading is S3d (WHOLE_WORLD.md §4, §12) |

### Found by the load test on the whole world (15 October 2026, S3b)

S3b ran every load test with 100 bots on the ball, at the landing site and on beaches all round it (GROUND.md §5.12),
and fixed on `whole-world` what broke there. Still open:

| What | Cause | Status |
|---|---|---|
| A moving character costs about half as much again in a snapshot as on the flat patch: 152 kbit/s down with 99 walkers in view (114 before S3), and the 1,200-byte budget holds about 87 of them a snapshot. | A position on a ball changes in all three coordinates, and the view's two octahedral halves both change as it turns (WHOLE_WORLD.md §8). | open: positions coded along the ground |
| When 80 bases start at once on beaches all round the world, 30 of 3,602 ticks go over 33 ms in receiving (the 99th percentile 27 ms). | A placement's checks against ground the tick hasn't touched yet took 3–30 ms each; a foundation's no longer asks about the ground (S3b). | open |
| Animals cost 2.0–2.7 µs each a tick, 2.1 on the flat patch. | Every look at the nav grid finds its face coordinates by two arctangents; on the plane it was a division. | open, small |
| The load test's bots share one copy of the building, which keeps one viewer's flags for a door (the last bot's that heard of it). | `BuildingReplica` keys door states by door only. The bots use their own doors by the verb they know (S3b). | open, bots only |

### Found while building IW1 (15 October 2026, ship spaces on `iw-spaces`)

| What | Cause | Status |
|---|---|---|
| In the ground game on the stand-in world (no surface view), every frame throws in `NatureView.Hook`, so the rest of the frame's drawing (people, things, the aim, building, raids, ships) never runs. | `GroundView` sets `NatureView.World` only when it opens a surface view; on the stand-in world `World` is null and `Hook` reads `World.Plants`. | done (15 October): `NatureView` skips the surface view's plants when there is none; S3c draws the world |
| Drawn people faced up to 60° away from where they looked on the ball. | `CharacterFrame`'s north is the flat world's −Z laid on the ground; the movement code's is the world's pole. | done for live people (IW1: `CharacterFrame.YawToward` in `PeopleView`); corpses, sleeping bags and other things drawn by a yaw keep the old convention |
| The ground game's sky has its horizon tilted on the ball. | The stand-in's procedural sky is the world's +Y up, not the landing site's. | done for now (15 October): the ground game turns its plain sky and sun to the up where the player is, every frame (`GroundView.TurnFlatSky`); S3c draws the world's own sky |
| An admin's ship spawn found no room for the Iron Wake near the landing site (58 of 60 spots had a tree in its footprint), and on a slope it rested on its hull with a velocity the contacts never let it move by, so it never parked. | The spawn's footprint test refuses any tree; the motor slept only on three loaded skids at under 5 cm/s. | done (IW1: trees and rocks in the way go for an admin's spawn; a ship held still with its engines off sleeps) |

### Found in play (15 October 2026)

| What the owner saw | Cause | Status |
|---|---|---|
| Hosting on `main` crashed while starting the world: Windows' error box for `Hollow.Server.exe` (exception 0xe0434352). | Both checkouts host with the same save (`user://hollow-host.db`). A `whole-world` build had upgraded it to schema 8; `main`'s server knows schema 6, threw, and nothing caught it. Leaving the world then threw again in `TerrainLod._ExitTree` (`SemaphoreSlim.Release(0)` when no worker had started). | done: each branch hosts with its own save, a server says why it refuses a newer save and stops cleanly (the menu shows it), the terrain's exit releases only started workers (GROUND.md §4.3); the upgraded save was moved to `hollow-host-whole-world.db` |
| The Iron Wake's hull is open at the bow and the stern, with see-through gaps all over it (the owner's screenshots, from outside). | Not yet traced. Suspected: faces whose winding flipped when the import baked the asset's mirrored parts, single-sided panels Godot culls from behind, faces lost in merging or LODs, or the loading mechanisms' cuts. | building (`iw-art2`) |
| The barrels look hollow behind: "as if the bullet is going to teleport; the barrel should be whole". The defense mounts' tubes end at a breech face with an open bay behind them, and a siege round crosses an open gap into its breech. | The loading mechanisms (IW-A part 2) brought the breech out of the closed receivers into open space. | building (`iw-art2`): whole barrels into closed breeches that open to take the round |
| After flying a Tin Finch, `kill` in the F1 console and a respawn, the game still thought the player was seated: E on a ship read "LAND FIRST: YOU CAN ONLY STEP OUT OF A SHIP AT REST". | Not yet traced: a seat or frame left on the respawned character, on the server or the client. | building (`iw2`) |
| Aiming at the Iron Wake's defense mount offers "BOARD IRON WAKE". | Every part of a ship offers the Tin Finch's whole-ship verbs; ships have one seat and four fixed parts. | building (`iw2`: seats and parts per ship) |
| The guns' dark steel reads as blotchy, square-patched rust up close. | Every steel finish derives from Poly Haven's Rusty Metal Sheet (the asset's material revision). | building (`iw-art2`: worn gunmetal and paint with rust at edges, seams and cavities) |

## 3. Deferred structure and decisions waiting on the owner

| What | Why it waits | Status |
|---|---|---|
| P6, one model for every world object | Each kind of object still has its own storage, network sync and save code; the biggest remaining piece, to plan with the owner. | wave 3, designing |
| P7, characters and held items | Grips, clipping, Rust's closed grip, first-person poses as data, full-body clips from local AI tools. | wave 2, designing |
| The rename from claim device to tool cupboard in the code | Deferred because another session worked in that code at the time. | open, small |
| The zone host, transfers between zones, the lifecycle with catch-up (GROUND_SYSTEMS.md K14) | Deferred by the owner on 14 October: one dedicated server for now. | deferred |
| SCALE.md | The scale proposal (2.4 km worlds, a galaxy of landmarks) decides findings 1 and 3. On 14 October 2026 the owner said go for S3 (one zone for a whole world, the globe map) and approved its design, WHOLE_WORLD.md; it was built on the branch `whole-world`, S3a (the zone is the whole world) and S3b (the load tests on the ball, GROUND.md §5.12), and merged into `main` on 15 October 2026 at the owner's word. S4 (the ground at walking scale) is to be talked through with the owner first. | S3 building (S3a and S3b on `main`; S3c and S3d next); the rest waiting |
| `prototypes/map-ux` | Three web prototypes of the galaxy map wait for the owner's choice. | waiting |
| The front door | The menu as the default start, a Developer page listing the viewers, a desktop shortcut (asked on 14 October after the F2 panel). | waiting |
| The owner's window size | Asked in the playtest plan and never answered; the frame-rate work depends on it. | waiting |

## 4. The plan (proposed 14 October 2026, 3:49 am; awaiting the owner's go)

**Wave 1, five packages in parallel, each on its own branch, each checked once (build, tests, one screenshot), merged
when green, then one playtest.**

| Package | Findings | Size |
|---|---|---|
| Seats, controls and damage | 28, 29, 30; keys with a state context; Space leaves any seat; only the damage pipeline changes health | medium |
| Building | 2, 4 to 11, 14; the kit re-imported with a duplicate-face check; sizes compared with the art | medium to large |
| Interaction and bodies | 12, 13, 15, 25, 27; naming; bodies that lie where the player died | large |
| Performance and hit feedback | 23, the frame rate, 19, 24; an upload budget; one's own hits predicted | medium to large |
| Weapons and gear | 16, 17, 18, 20, 26; attachment slots; the protection panel | large |

**Wave 2, designs first, each for the owner to approve:** the world at walking scale (now SCALE.md); gunplay feel after
a study of Rust 2017 to June 2022 (finding 21); characters and animation (P7: 16, 22 and the wardrobe hookup).

**Wave 3:** P6, the shared object model, alongside the world work; then monuments in the game and more worlds.

**Defaults proposed with the plan, not yet taken:** Space leaves any seat, the ship's up and down move to Shift and
Ctrl; a footing about 3 m deep (Rust's number to be checked); a floor is held by every wall it rests against; attachment
slots in the gun's panel; a one-slot fuel tank opened from the fuel cap; recoil as in old Rust; bodies lie where the
player died, are harvested, then become a bigger bag; a protection panel with Rust's damage types; plants stay made in
code and the scans go into ground, cliffs, rocks and deadwood after a licence check.

## 5. Done since the list was made

- The first playtest's findings (13 October: looking, holding, free foundations) were fixed in `ground-look` and
  merged (GROUND.md §11's decisions for `ground-look`).
- The 30 merged worktrees under `Hollow-wt` were removed (14 October).
- The kernel (GROUND_SYSTEMS.md §12), the F2 developer panel (GROUND.md §8.5) and the merge into `main` (14 October):
  architecture and tools, not fixes to the findings above.
