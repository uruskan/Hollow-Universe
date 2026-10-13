# Hollow Universe — the ground game's shared systems

Status: **approved by the owner on 13 October 2026**, with the defaults of §11; being built (§10). It changes how the
ground game ([GROUND.md](GROUND.md)) is put together, not what it plays like. Where it and GROUND.md disagree on
structure, this document wins.

---

## 1. Why

On 13 October 2026, after the first playtest, the owner set the rule:

> We shouldn't add stuff just to "make it happen". We should have proper, optimized, efficient, modular, scalable,
> non-repeating systems and architecture. This is a huge online networked game and we are going to add many more
> features; we shouldn't write code with 20 if-elses or switch cases.

The ground game was built as vertical slices, one work package per step (survival, building, guns, raiding, ships, world).
Inside each slice the numbers are data and no rule tests an item by name. But each slice built its own copy of the same
cross-cutting systems. Four read-only surveys of the code (13 October) counted them:

| Concern | Copies today | What the playtest found because of it |
|---|---|---|
| Naming a world thing | 4 id resolvers (entity store, building, animals, a linear scan for explosives); `TraceHit` has one field per family | — |
| Interaction (E) | ~25 verbs over 11 messages; 4 pickers (`Client.Aim`, `AimShip`, `AimWorkbench`, `BuildView.Interactable`) tried in a hand-ordered chain; 3 option UIs (`OptionsMenu` list, plan/hammer wheel, keypad) | no cohesive hold-E menu; a code lock can't get a code; hold-E on the cupboard never shows its menu; an open garage door can't be aimed at, so it can't be closed |
| Placement | 7 intents, 2 answer types; 2 ghosts (deployables without validity, pieces with); terrain-only aim for deployables; 3 reach rules (3 / 4.5 / 8 m); water checked by one path only | nothing stands on a foundation; no ghost for doors, the garage door, locks, the cupboard, charges or ships; the Tin Finch item can't be placed |
| What a held item does | chains in `GroundView.ReadInput`/`UseHeld`, `BuildView.Use`, `FirstPersonView` (incl. `Key == "torch"`, `"rifle"`), and the server's melee/bow chain | R can't open an ammo menu; freelook would need checks in many places |
| Item information | one chain covering 7 parts; guns, rounds, attachments, explosives, deployables, building items, ships, blueprints and components show nothing | "information on most stuff is missing"; a blueprint has no LEARN button |
| Answers to intents | `IntentResult`/`ActionResult` and `BuildResult`/`Refusal`, two text tables | — |
| Viewer-specific facts | `ThingFlags.Mine`, `ClaimNet.Authorised`, `ShipNetFlags.Mine`, `DoorNet.CanUse`, nothing for a door's owner or code | the code-lock dead end |
| Damage | melee, arrows, bullets and blasts each switch over every target family | nothing knows that a thing was destroyed (no gibs) |
| Crafting stations | a workbench only gates recipes | a workbench doesn't speed crafting |
| Network and saving | 7 per-session sent-state structures, 5 relevance mechanisms, 7 hand-written codecs, 6 save-diff strategies, 2 interpolators | every new kind of object repeats all of them |

The goal: **each concern is implemented once.** New content is data. A new kind of behaviour is one handler, registered
in one place.

## 2. Principles

1. **Capabilities in data.** A thing's behaviour is the set of its capabilities: holds items, opens and closes, takes a
   lock, authorises builders, burns fuel, is a crafting station, can be picked up, fires, places, is eaten, takes damage,
   breaks into pieces. Each is declared as a block in the thing's definition (items.json, deployables.json,
   building.json, ships.json, world.json, gather.json, explosives.json), as most already are. Code has one handler per
   capability, found in a registry.
2. **One path per concern.** One way to name a target, aim at it, interact with it, place something, describe an item,
   damage something, report that it was destroyed.
3. **Same rules on both sides.** Prompts, menus, ghosts and their red/green verdicts come from the same
   `Hollow.Simulation` code the server validates with. The server stays authoritative.
4. **Registries, not chains.** Routing a network message by its type is transport and stays. A switch or if-chain over
   item kinds, target families or object types in rules, client logic or views is not allowed; it becomes a lookup.
5. **Stable wire numbers from data.** Lists that go on the wire (verbs, capabilities, placeables) are append-only lists in
   the data, whose index is the wire number, as gather kinds already are. A load-time check makes sure every entry has
   exactly one handler and every handler an entry.
6. **Budgets.** Each system states its cost per frame (client) and per tick (server) and is measured once.

## 3. Shared foundations (package P0)

### 3.1 Target references
`TargetRef { Family : byte, Id : ulong, Part : byte }` names anything in the zone: a character, an animal, a natural
object, a thing (deployable, drop, bag, corpse, prop), a building piece, a door, the tool cupboard, an explosive, a ship.
`Part` names a sub-target (a ship's hatch, fuel cap or cargo; a door's lock). Ids are already 64-bit and unique across
families, so the family only routes the lookup.

Each family registers an `ITargetFamily`: resolve an id, its pose, the nearest point of its bounds to a point, whether it
still exists, its capabilities, its model key. This replaces the four resolvers and the per-family fields of `TraceHit`.
Interaction, placement supports, damage receivers and the Destroyed event all use it.

### 3.2 One answer vocabulary
`ActionResult` and `Refusal` merge into one `Outcome` code list. `IntentResult { Seq, Code, Detail }` answers every
intent; the texts move to one table in data (`texts.json`), so the interface has one place to look and a later
translation one file to translate.

### 3.3 Viewer flags
Every replicated thing carries one `ViewerFlags` byte computed for each viewer by its capabilities: `Mine`, `Authorised`,
`HasCode`, `Locked`, `CanUse`, `Empty`, `Lit`, `Teammate`. This replaces the four ad hoc flags above and gives the client
what it needs to offer the right options; it is what the code lock was missing.

### 3.4 Reach and sight
One rule: the distance from the eye to the target's nearest surface, within the verb's or placeable's reach from data
(defaults: interact 3 m, deployables 3 m, building 8 m, as now), and one line-of-sight ray. This replaces about ten
scattered slack constants (`+0.3`, `+0.5`, `+1.5`, `+3`, `+4`) and adds the missing sight check to interactions.

### 3.5 Registries
A small `Registry<THandler>` keyed by the data list's index, filled at start-up, validated against the data. Used by
every system below.

### 3.6 Support links
A world thing may rest on or be stuck in another: a furnace on a foundation, a lock on a door, a charge on a wall, an
arrow in a tree, a wall or a deer's flank. `SupportLink { Support : TargetRef, local pose, bone }` records it once, the
same way for all of them. A linked thing is drawn and found where its support is now (on a moving body, at that bone),
and when its support is destroyed or removed, its dependents go with it by one rule (Rust: what stands on a foundation
goes with it; an arrow in a body that is gone falls with the body's loot rules). Placement (§6) creates links;
interaction (§5) reaches linked things where they are.

### 3.7 Lifetimes
One despawn rule for every loose thing in the world: a `lifetime` from data (survival.json today: dropped items and bags
300 s, bodies 300 s, spent arrows), counted on world time (it pauses while the server is down), restarted by what the
data says restarts it (a bag's newest item), and ended at once when the thing is emptied. One step in the zone runs it
for every thing that has a lifetime; bags, bodies and arrows don't each keep their own timer code.

## 4. One radial menu (package P5, first)

One immediate-mode control in RustMVP's look (its pictures: RustMVP
`docs/evidence/building-repair-2026-10-04/door-lock-context.png`, `hammer-upgrade-wheel.png`,
`docs/evidence/building-2026-10-04/plan-wheel-build4-image-1.png`). Options carry a glyph or item icon, a title, a
description, a cost, and enabled-or-the-reason-not. Hold to open, the mouse picks, release confirms, Esc or the right
mouse cancels. The menu takes an options provider, not a kind.

Opened by: holding E (interaction), the right mouse with the building plan or hammer (as now), holding R with a gun (the
ammo menu). Replaces `GroundUi.Wheel`'s closed `WheelKind { Plan, Hammer }` and the vertical `OptionsMenu` list.

**As built** (P5, 13 October 2026, `ground-radial`; GROUND.md §8.4, decisions Q1–Q9). The contract is plain C# in
`game/scripts/UI/Model/RadialTypes.cs`: `IRadialSource` (the options, the current one, `Choose(index)`, `Cancel()`),
`RadialOption` (an icon, by a glyph's name or an item's key; title, description, cost line; the reason it can't be
chosen), and `RadialBinding` (a held input, its hold time, what it opens now), listed by the view model in
`IGroundUi.Radials`. The control is `GroundUi.Radial.cs`; the glyphs are named (`RadialGlyphs`). The plan and the
hammer are two sources, `PlanRadial` and `BuildingRadials.Hammer`, shared by the game and the build sandbox; `WheelKind`
and its per-kind code are gone. A tap of a held key chooses the source's current option; a disabled option says why.
The viewer proves the E and R bindings on samples (`--screen options`, `ammo`; `--ui-check`).

For P1: give `ClientGroundUi.Radials` a `RadialBinding(RadialInput.Interact, holdSeconds, open)` whose `open` makes a
source for what is aimed at at the press (none: E does nothing); its options come from the capabilities' `Offer`, its
`Current` is the primary verb (a tap does it), `Choose` sends the verb (a verb that needs a code opens the keypad
first), and the verbs' glyphs are names in interactions.json. Then delete `OptionsMenu` and the hold-E code in
`GroundView`. For P3: the held behaviour's handler gives the right mouse's and R's sources the same way; the ammo
radial's options use `RadialIcon.Item` with the round's key, and its `Current` is the loaded round, so a tap of R
reloads with it.

## 5. Interaction (package P1)

**Verbs** are an append-only list in `interactions.json`: key, label, glyph, whether it needs a code (the keypad opens
first), its reach if not the default, and whether the wounded or the seated may use it. `holdSeconds` is 0.35 (RustMVP's
tap/hold line).

**Capability handlers** implement two methods, used by the client to build the menu and by the server to check and act:

```csharp
void Offer(in InteractContext ctx, List<InteractOption> into);   // options now, enabled or with a reason; which is primary
Outcome Do(in InteractContext ctx, VerbId verb, in InteractArgs args);
```

| Capability (where its data is) | Verbs |
|---|---|
| Container (`slots` on deployables, props, corpses, sleepers, bags, ship cargo) | Open |
| Door (building.json doors) | Open, Close; Pick up (open, no lock) |
| Code lock | Set code (a fresh lock, its owner: primary), Enter code, Change code, Lock (once it has a code), Unlock, Remove |
| Key lock | Lock, Unlock (with its key), Remove, Cut a key |
| Tool cupboard | Authorise (primary until authorised), Open, Deauthorise, Clear list, Pick up |
| Burner (`device`: campfire, furnace) | Light, Put out; Open |
| Machine (recycler, research table) | Start, Stop; Open |
| Station (`workbench`) | Tech tree |
| Pickup (gather.json pickups and bushes, drops, spent arrows) | Pick up |
| Picked-up deployable (new flag `pickup`) | Pick up (empty, and authorised where a cupboard covers it, as in Rust) |
| Wounded character | Help up |
| Character (when the viewer leads a team) | Invite to team |
| Dud explosive | Relight |
| Ship (by part) | Hatch, Board, Refuel, Cargo, Lock |

**Spent arrows** (the owner, 13 October 2026) stay where they stuck: in the ground, a building, a deployable, a tree, an
animal or a player, alive or dead, held there by a support link (§3.6) and riding on a moving body. Each is its own small
target; a tap of E on it picks it up, from a living player or animal too, and the shared lifetime (§3.7) removes it
later. They no longer go into bags.

**Targeting:** one `Aim(eye, direction)` asks every registered family for its nearest hit along the ray and takes the
nearest of all, not the first in a priority list. A door's target includes its opening, so an open garage door, whose
slab has rolled up out of the ray, is still aimed at.

**Input:** a tap does the primary option; holding opens the radial with every option, disabled ones showing why. The
prompt reads `E  OPEN DOOR`, with the hold hint when there is more than one option.

**Wire:** one `InteractIntent { Seq, Target, Verb, Arg, Code }`. It replaces PickUp, Open, Toggle, HelpUp, Rearm,
Team (invite), Ship (on foot), Door (toggle, pick up), Lock (except fitting), Claim (except placing) and Unlock (the tech
tree's research is a station verb with the recipe in `Arg`). Closing a loot panel and the seated pilot's keys stay as
they are.

**Server:** resolve the target → the verb's gates (alive, awake, wounded) → reach and sight → the capability's `Offer`
must contain the verb, enabled → `Do` → `IntentResult`.

**Fixes:** a fresh code lock offers Set code first; the garage door closes; the cupboard's menu appears; the claim device
is renamed the **tool cupboard** (data, interface and code).

**Cost:** client targeting under 0.1 ms a frame; server one lookup, one `Offer`, one ray per intent.

## 6. Placement (package P2)

**A rule per placeable**, declared in data next to the thing (defaults in one `placement` block):

| Field | Meaning |
|---|---|
| `surfaces` | where it may stand: `terrain`, `floor` (tops of foundations, floors and stairs' landings), `wall`, `opening` (doorway or wall frame), `door` (a door leaf), `any` (pieces, doors, deployables, trees, rocks, terrain) |
| `solver` | `free` (onto the surface under the aim), `socket` (building pieces), `opening`, `attach`, `sweep` (charges) |
| `footprint` | half extents, or the model's hull from the catalogue (ships) |
| `support` | `corners` (every footprint corner supported, RustMVP) or `centre` |
| `maxSlope`, `maxStep`, `water` | as today's deployable fields; water is now checked for every placeable |
| `align` | `up` (the planet's) or `normal` (charges on walls) |
| `rotation` | `free`, steps in degrees, `support` (takes the support's yaw), `flip` (doors) |
| `reach` | defaults 3 m for deployables, 8 m for building, as now |
| `privilege` | `needed` (authorised where a cupboard covers), `blocked` (refused in others' cover), `ignored` (charges) |
| `limits` | per structure (Rust: 20 deployables, one cupboard), per player (sleeping bags) |

**Solvers and checks** are registered by name. A solver turns the aim into a candidate (pose, support, socket); a fixed
pipeline of data-configured checks (reach, sight, surface and support, slope and step, water, overlap, privilege,
limits) gives the verdict and its reason; the building solver adds its own checks (stability, sockets). The client runs
it every frame for the ghost against its replicated world (terrain, the building mirror, things' boxes, characters,
claims); the server runs it again and accepts only a pose within a small tolerance of its own (RustMVP's rule).

**One ghost:** the thing's catalogue model in a hologram material, one colour when it fits and red when not, with the
reason under the crosshair (the building plan's line), and a pending look until the server's answer arrives. R turns it
by its rule. Building pieces keep their batched ghost mesh in the same material.

**Wire:** one `PlaceIntent { Seq, From, Slot, Expected, Placeable, Support, Socket, Pose, Variant }`, replacing
PlaceIntent, PlacePieceIntent, DoorIntent.Place, LockIntent.Attach, ClaimIntent.Place and PlaceChargeIntent. Throwing
stays its own action and keeps the shared sweep for sticking.

**Support links:** what stands on a piece is linked to it, and goes when the piece goes (Rust).

**Fixes:** furnaces, boxes, campfires and bags stand on foundations and floors; every placeable has a ghost with a
verdict, the garage door, locks, the cupboard, charges and the Tin Finch included; one reach, sight, water and privilege
rule; the Tin Finch is placed from its item (`ships.json` gets a placement rule with the hull as its footprint, and the
ship family's placement handler calls `SpawnShip`).

**Cost:** client solve and ghost under 0.2 ms a frame.

## 7. Held items, item information, inventory actions (package P3)

**Held behaviour from parts.** Each item part that gives a held behaviour registers one handler: melee, bow, firearm,
throwable, placer (deployables, doors, locks, the cupboard, ships), planner, hammer, consumable, blueprint. A load-time
check allows at most one per item. The handler owns the client's mouse, R tap, R hold and radial options, the first-person
pose kind, and on the server the per-tick buttons and the validation of its intents. Freelook (Alt) blocks actions in
one place, the controller that asks the handler.

**Information rows.** Each part registers a describer: guns (damage, rate of fire, magazine, reload, aim cone, range,
recoil), rounds, attachments, explosives (damage to structures, fuse), deployables (slots, station level, burner),
building items (door health, lock kind), ships (hull, fuel, top speed), blueprints (what they teach), components (what
uses them), plus today's melee, bow, food, wear, fuel and cooking rows.

**Inventory actions.** Each part registers its buttons (USE or EAT, WEAR, LEARN, UNLOAD, REMOVE <part>), sent by one
`ItemActionIntent { Seq, From, Slot, Expected, Action, Arg }` that replaces UseIntent and DetachIntent. DROP and the
split stay generic.

**Guns.** A small icon of the loaded round under the gun's icon in any slot (the count stays at the bottom right). Hold R
opens the ammo radial: every round of its calibre the player carries, with counts, and UNLOAD; a choice reloads with it
(`LocalGun.Reload(ammo)` already exists).

**Dropping.** Releasing a dragged item anywhere that isn't a slot drops it, over panels and buttons too; releasing it
over its own slot cancels (Rust). Today a release over any panel silently cancels.

**Workbench speed.** The station capability scales a job's time by how far the bench's level is above the recipe's
(Rust's: one level above, half the time; two or more, a quarter; in data), fixed when the job starts.

**Replaces:** the chains listed in §1 (input, `BuildView.Use`, `FirstPersonView`, the server's melee/bow chain,
`Describe`, `MoreActions`), and the `Key == "torch"` and `"rifle"` checks, which become model-catalogue flags.

## 8. Damage and destruction (package P4, built: GROUND.md §6.4.2)

One damage request (who, with what, how delivered, the damage channels, amount, body region, point, direction); a
registry of receivers by target family, each with its own defences and consequences; every delivery (melee, arrow,
bullet, blast) calls one entry point; one consequence step emits **Destroyed** `{ Target, model key, pose, direction of
the blow, cause }` to everyone near. Gibs, effects, sounds and support links listen to it. Built by `ground-damage`
(GROUND.md §6.4.2, decisions D6–D11): receivers are keyed by `HitTarget` and each finds its own things along a ray and in
a blast's reach; with P0 their routing moves onto `TargetRef`.

## 9. Later: one model for every world thing (package P6)

The fourth survey found every family repeating store → dirty marker → prepare and replicate → receive → view → codec and
save diff. The end state: one entity store for every world thing, with components declared per archetype in data; one
relevance grid; replication and saving written once per component (NetGen extended to generate save codecs); one
interpolator; one client view per capability. Building pieces keep their structure solver (sockets, stability) behind the
same capabilities. This is the largest and riskiest change; it is decided after P0–P5 have proved the capability model.

## 9a. Later: characters and held items (package P7)

The owner's playtest (13 October 2026) found the characters' presentation unpolished: items clip into the third-person
body or are held oddly, the first-person rock wants Rust's closed grip, and a swing's visible contact misses the point the
server hits (the pickaxe's tip doesn't meet the hotspot). First and third person are two presentations of the same body
asset, as in Rust: the player's own forearms and hands on the camera (key poses, 60° field of view) and the full body
for others (retargeted clips and IK). The package gives both one set of grip points and sockets per item (from the
model catalogue), hand IK onto them in third person, held-item clips per kind of held behaviour (§7), and swings whose
contact frame and point come from the same hit the server computes. To be planned with the owner.

## 10. Order of work

1. Merge the five packages of the first playtest: freelook, hands and foundation height; exact recoil and the damage
   request (P4); tree X and node hotspots; loot bags (all merged 13 October 2026); gibs. The Tin Finch's placement waits
   for P2.
2. **P0** foundations, then **P5** the radial.
3. **P1** interaction, **P2** placement and **P3** held items, in parallel.
4. Merge, then the owner plays.

Every package adds its shared system with engine-free tests, moves every existing path onto it, deletes the old path
(its messages, enums, pickers, UI), bumps the protocol, and updates GROUND.md as built. The network and persistence tests
(10,000 lines) guard the behaviour; the content check gains a registry-completeness check. Each package verifies once in
the game with one screenshot.

## 11. Defaults the owner may change

1. Hold E for 0.35 s opens the radial (RustMVP); the radial looks like RustMVP's.
2. Every floor-standing deployable may stand on foundations and floors; nothing hangs on walls yet.
3. Deployables can be picked up like Rust's: hold E, Pick up, when empty and authorised. This covers boxes, furnaces,
   campfires, workbenches, bags, beds and the cupboard; doors (open, without a lock) and locks have their own verbs.
4. Workbench speed: one level above the recipe halves its time, two or more quarter it.
5. The loaded round's icon sits at the bottom left of a gun's slot.
6. UNLOAD is in the ammo radial and in the inventory's ACTIONS.
7. A drag released anywhere but a slot drops the item.
8. The claim device is called the tool cupboard everywhere, code included.
