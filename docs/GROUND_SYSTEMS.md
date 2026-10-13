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

## 3. Shared foundations (package P0, built 13 October 2026: GROUND.md §4.2, decisions E1–E13)

### 3.1 Target references
`TargetRef { Family : byte, Id : ulong, Part : byte }` names anything in the zone: a character, an animal, a natural
object, a thing (deployable, drop, bag, corpse, prop), a building piece, a door, the tool cupboard, an explosive, a ship.
`Part` names a sub-target (a ship's hatch, fuel cap or cargo; a door's lock). Ids are already 64-bit and unique across
families, so the family only routes the lookup.

Each family registers an `ITargetFamily`: resolve an id, its pose, the nearest point of its bounds to a point, whether it
still exists, its capabilities, its model key. This replaces the four resolvers and the per-family fields of `TraceHit`.
Interaction, placement supports, damage receivers and the Destroyed event all use it.

*As built:* `Targets/TargetRef.cs` and `Zone.Targets.cs`. Eleven families (`TargetFamily`: `Ground`, `Character`, `Animal`,
`Natural`, `Thing`, `Piece`, `Door`, `Cupboard`, `Explosive`, `Ship`, and P1's `Arrow`; `None` names nothing), each one
class registered once by its number. `Part` 0 is the whole thing and n is part n − 1: a door's leaf, a body's bone in its hit shapes'
order (`HitShape`, one list the ray test, the nearest point and the bone poses share). Families also say whether their
things move (§3.6) and take them out of the world (`Remove`, §3.7). Every world thing knows its `Ref`; `Zone.Resolve`,
`Exists`, `TryPose` and `TryNearest` route by family, and the explosives' scan is a dictionary. The damage receivers are
registered by family, `TraceHit` carries one `TargetRef`, and `HitTarget` and `DestroyedKind` are gone: the wire
carries the family in 4 bits (`HitMarker`, `ProjectileEnd`, `Detonation`) and `TargetNet { Family, Id, Part }`
(`ThingDestroyed`). Test: `FoundationTests.EveryFamilyNamesItsThings`.

### 3.2 One answer vocabulary
`ActionResult` and `Refusal` merge into one `Outcome` code list. `IntentResult { Seq, Code, Detail }` answers every
intent; the texts move to one table in data (`texts.json`), so the interface has one place to look and a later
translation one file to translate.

*As built:* `Outcome.cs`: `OutcomeCode`, 73 codes with fixed values, append only (7 bits on the wire), and `Outcome
{ Code, Detail }`, the detail being the one number a line names (an item, seconds, a percentage, a level).
`ActionResult`, `Refusal`, `BuildResult` (message 95, free) and the building's own answer path are gone; the client
raises `IntentAnswered` and `IntentRejected` for every intent, and the game shows every refusal through
`ClientGroundUi.OnRejected` with one error sound. `game/data/ground/texts.json` holds the lines by code name
(placeholders `{n}`, `{item}`, `{item|word}`), read with the data but not hashed; the content check wants exactly one
line per code. The gun's shots and reloads keep their own `GunResult` (its prediction rolls back by itself; P3). Test:
`FoundationTests.EveryAnswerHasALine`.

### 3.3 Viewer flags
Every replicated thing carries one `ViewerFlags` byte computed for each viewer by its capabilities: `Mine`, `Authorised`,
`HasCode`, `Locked`, `CanUse`, `Empty`, `Lit`, `Teammate`. This replaces the four ad hoc flags above and gives the client
what it needs to offer the right options; it is what the code lock was missing.

*As built:* `game/data/ground/capabilities.json` (hashed, append only, at most 64) lists `owned`, `container`, `burner`,
`machine`, `door`, `key-lock`, `code-lock`, `cupboard`, `ship`, `lifetime` and `explosive` (P1 appends `pickup`,
`portable`, `station`, `wounded`, `character`, `dud` and `arrow`, §5); each has one handler
(`Capabilities/Handlers.cs`) in a sealed registry, and the zone won't start while one is missing or extra. A thing's set
(`CapabilitySet`, a 64-bit mask) comes from its definition's blocks (`CapabilityData.Of`: slots, device, recycler or
research) or, for what the rules make, capabilities.json's own sets (bags, bodies, doors, the cupboard, ships,
explosives, duds, carcasses); a door's adds its lock's. `Zone.FlagsFor(target, viewer)` joins every capability's share
for a viewer (`Viewer`: ID, team, the keys they carry). The byte rides on `ThingSpawn` and `ThingUpdate`, `DoorNet`,
`ClaimNet` and `ShipNetState.Viewer`; a thing goes again when its contents or the viewer change (a team, a key). A
door speaks for its lock (Mine: the lock's fitter), so a fresh code lock's owner is offered SET CODE first and LOCK only
once it has a code (the code lock's handler, §5). Tests: `ViewerFlagsComeFromCapabilities`, `ViewerFlagsReachEachPlayer`.

### 3.4 Reach and sight
One rule: the distance from the eye to the target's nearest surface, within the verb's or placeable's reach from data
(defaults: interact 3 m, deployables 3 m, building 8 m, as now), and one line-of-sight ray. This replaces about ten
scattered slack constants (`+0.3`, `+0.5`, `+1.5`, `+3`, `+4`) and adds the missing sight check to interactions.

*As built:* `Zone.Reach(eye, target, reach, sight)` (`Zone.Reach.cs`): the distance to the target's nearest surface
(its family's `TryNearest`), and one ray to 0.1 m short of that point that meets no building body but the target's own
(by ID) and no ground within 0.25 m of it. survival.json's `reach`: interact 3, open 3.5, keepOpen 4.5 (no sight), help
3, invite 6, station 4 (no sight). Opening, looting and keeping a container open, picking up, lighting, doors, locks,
the claim device, helping up, lighting a dud again, team invites and a workbench's level all use it; interactions
through walls are refused, and the client's one targeting query (§5) meets walls, so it no longer offers what the server
would refuse. Every verb names its reach (interactions.json, §5); a ship's parts use it since P1. Placement (deployables
3 m, building 8 m) keeps its own until P2. Test: `FoundationTests.ReachIsToTheNearestSurfaceInSight`.

### 3.5 Registries
A small `Registry<THandler>` keyed by the data list's index, filled at start-up, validated against the data. Used by
every system below.

*As built:* `src/Hollow.Simulation/Registry.cs`: handlers in an array by a data list's index or an enum's number, sealed
after start-up with its problems named (an entry without a handler, a handler for no entry, two for one). The target
families, the damage receivers and the capabilities use it.

### 3.6 Support links
A world thing may rest on or be stuck in another: a furnace on a foundation, a lock on a door, a charge on a wall, an
arrow in a tree, a wall or a deer's flank. `SupportLink { Support : TargetRef, local pose, bone }` records it once, the
same way for all of them. A linked thing is drawn and found where its support is now (on a moving body, at that bone),
and when its support is destroyed or removed, its dependents go with it by one rule (Rust: what stands on a foundation
goes with it; an arrow in a body that is gone falls with the body's loot rules). Placement (§6) creates links;
interaction (§5) reaches linked things where they are.

*As built:* `Targets/SupportLink.cs` (`Support`, a `TargetRef` whose part names a door's leaf or a body's bone, and the
pose there) and `Zone.Supports.cs`: `LinkTo`, `TryPose(link)` (where the support or its bone is now), `Hold`, `Release`
and `DependentsOf`. When a support goes (the Destroyed event; a static thing taken away; a piece, door or claim device
removed or starting to come down; a tree felled; an animal or a character gone) each dependent meets its own
capability's consequence (`IDependentCapability.SupportLost`). Explosives are the first dependents: their `SupportKind`
switch and fields are gone, a charge follows a door's leaf and falls when its support goes, and `ExplosiveCodec`
version 2 stores the `TargetRef` (version 1 records come back onto one). Spent arrows are the second (P1, §5): one rides a
body's bone and falls to the ground when its holder goes. Placement (P2) adds the next. Test:
`FoundationTests.SupportLinksFollowAndGoWithTheirSupport`.

### 3.7 Lifetimes
One despawn rule for every loose thing in the world: a `lifetime` from data (survival.json today: dropped items and bags
300 s, bodies 300 s, spent arrows), counted on world time (it pauses while the server is down), restarted by what the
data says restarts it (a bag's newest item), and ended at once when the thing is emptied. One step in the zone runs it
for every thing that has a lifetime; bags, bodies and arrows don't each keep their own timer code.

*As built:* survival.json's `lifetimes` (bag 300 s, restarted by a deposit; body 300 s; dud 600 s; P1's spent arrow
300 s) and a species' `corpse` seconds in animals.json for a carcass; all but the dud go at once when emptied. The `lifetime` capability's
handler decides (`ILifetimeCapability`: over, restart, end) and the thing keeps its state (`ILifetime`: rule, end tick,
emptied). The zone keeps the loose things in one list, filled as they come (a bag or body added, a charge gone out, an
animal dead, each restored from storage) and left as they go; one step once a second ends those whose time is up, each
through its family's `Remove`. Looting a bag or body empty, or harvesting a carcass out, ends it at once. Gone:
`DroppedContainer.Expired`, `StepDespawns` and its scan of every static thing, the duds' `DudSeconds` timer and the
animal world's corpse check. The devices' step now looks at the burners and machines alone, `BagsNear` at the loose
things. Test: `FoundationTests.LifetimesAreOneStepForEveryLooseThing`.

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
The viewer proves the held keys on samples: E opens a door's options, R with the pistol its rounds (`--screen options`,
`ammo`), and `--ui-check` plays E's tap, hold, choice and cancels.

For P1 (done, §5): `ClientGroundUi.Radials` has a `RadialBinding(RadialInput.Interact, holdSeconds, open)` whose `open`
makes a source for what is aimed at at the press (none: E goes to the game); its options come from the capabilities'
`Offer`, its `Current` is the primary verb (a tap does it), `Choose` sends the verb (a verb that needs a code opens the
keypad first), and the verbs' glyphs are names in interactions.json. `OptionsMenu` and the hold-E code in `GroundView`
are gone. For P3: the held behaviour's handler gives the right mouse's and R's sources the same way; the ammo
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

**As built** (P1, 13 October 2026, `ground-p1`; GROUND.md §6.1–§6.8, §8.4, decisions I1–I17).

*Verbs* (`game/data/ground/interactions.json`, hashed, append only: an entry's index is its number on the wire): 39, each
with a key, a label, a glyph (`RadialGlyphs`), a line for the radial, a priority (the ring's order and which primary a
tap does), a reach by name from survival.json's (interact by default), whether it needs sight, a code, a panel (the
tech tree), whether it is in the radial, a device switch (on, off), and its gates (alive and awake unless it says not;
the wounded and the seated only where it allows). Doors, locks, the cupboard and deployables each have their own verbs
where they are done differently (`door-pick-up`, `cupboard-pick-up`, `retrieve`; `key-lock`, `code-lock`), with the same
labels. Read by `Content/Interactions.cs` (`VerbDef`, `InteractionData`).

*Handlers:* a capability with verbs implements `IInteractCapability { Verbs, Offer, Do }` (`Interaction/Interaction.cs`);
the handlers are in `Capabilities/Interactions.Things.cs` (container: open or loot; pickup: pick up, pick; portable:
retrieve; burner: light, put out; machine: turn on, off; station: tech tree, unlock), `Interactions.Building.cs` (door,
key lock, code lock, cupboard) and `Interactions.World.cs` (wounded: help up; character: invite; dud: light; ship: hatch,
board, refuel, lock; arrow: falls when its holder goes). Each names its verbs by key and finds what one does in a table
by number (`VerbActions`), never by testing which it was given. `InteractionRules` gives every verb its one handler in a
sealed registry (a verb without one, one with two, or a handler's verb the data lacks stop the start), and makes a
target's options: every capability's offer, each verb's gates, ranked by priority; `Primary` is the first primary that
can be done, else the first primary (the tap says why), else none. New capabilities: `pickup` (gather.json's pickups
and bushes, bags, arrows), `portable` (deployables.json `pickup`, on all eleven), `station` (`workbench`), `wounded`,
`character`, `dud`, `arrow`. New answers: `Seated`, `NotEmpty`.

*What a handler knows:* `TargetFacts` (`Targets/Targeting.cs`): its capabilities and viewer flags, name, count, whether it is
a person's (looted, not opened), open, busy, at rest, lock and definition, from its family (`TryFacts`) on the server and from the replicas on a client
(`GameClient.Targets.cs`, one view per family), so `Offer` runs the same on both. `InteractContext` adds the doer
(`Doer`: dead, asleep, wounded, seated; in a team, its leader, a full team) and the building (rights at a place), and on
the server the zone, the player and the thing.

*Targeting:* every family is an `ITargetView` (`TryPose`, `TryNearest`, `Aim`, `TryFacts`; `ITargetFamily` extends it), and
`Targeting.Aim(views, ray)` keeps the nearest hit of all, as far as the longest verb reaches, past the one looking. The
shapes are shared so both sides meet the same thing (`AimShapes`: boxes, balls for bags and bodies, capsules for arrows;
`DoorShapes`, a door's leaves and its opening; `ShipShapes`, the hull and its parts; `NaturalAim`; `BuildingAim`, one
building ray; `GroundAim`). A hit says what it is to the view (`AimKind`): something to act on, a backdrop (the ground,
pieces, trees and rocks, animals: they stop the view and offer nothing) or an open door's opening, through which the view
goes on: the door is met there only when nothing beyond is to act on. `Zone.Aim`, `GameClient.Aim` and
`GameClient.AimInteraction` (the target, its facts, its options within each verb's reach, the primary: the prompt's and
the radial's), 23 µs a frame in the test's fan of views.

*The intent:* `InteractIntent` (210) `{ Seq, Target, Verb, Arg, Code }`, sent by `LocalPlayer.Interact(target, verb, arg,
code)`; on the server `Zone.Interact` (`Zone.Interaction.cs`): a verb it knows, the gates, a panel's verb refused (the
interface's own), the target, `Zone.Reach` with the verb's reach and sight, the offer holding the verb enabled (else
`NotUsable`, or the offer's reason), the handler's `Do`; answered by `IntentResult`. The tech tree's unlock is the
station's `unlock` with the recipe in `Arg`, sent to the best bench in reach (`GameClient.BenchNear`). Deleted: messages
56 (Open), 58 (PickUp), 60 (Toggle), 66 (HelpUp), 112 (Rearm), 130 (Unlock); `TeamAction.Invite`; `ShipAction.Board` and
`Refuel` (the seat's hatch, exit, lock and descend stay); `DoorAction` but placing, `LockAction` but fitting,
`ClaimAction` but placing; the zone's `Open`, `PickUpNatural`, `PickUpItem`, `Toggle`, `Rearm`, `Board`, `RefuelShip`
and ships.json's `reach`; the client's `Interaction`, `InteractionKind`, `ShipInteraction`, `AimShip`, `AimAtDuds` and the
intents' methods. Protocol 18 (P3 took 17).

*The interface:* `ClientGroundUi.Interaction.cs`: each frame the prompt ("E  OPEN WOODEN DOOR", or the label alone where
the verb says so: "E  SET CODE"; the reason when the tap can't; the hold hint with more than one option); E's binding makes an `InteractionRadial` of the target at the press,
read again each frame from where the player is; a code verb asks the keypad first (`GroundView.ShowKeypad(title,
sends)`), a panel verb opens its panel by name, the loot panel's fire and machine switch finds its verb by `switch`.
`OptionsMenu`, BuildView's door and cupboard options and GroundView's hold-E chain are gone. A radial no longer stops
walking (`GroundUi.TakesKeys`: screens and chat take the keys, a radial only the pointer and buttons). The claim device
is called the Tool Cupboard wherever a player reads it (code names stay until the rename).

*Spent arrows* (`Zone.Arrows.cs`, `World/SpentArrow.cs`): an arrow that stays whole (items.json `recover`, 0.5) sticks in
what it hit, its tip survival.json's `arrows.depth` (0.1 m) in along its flight, linked to it where the shooter saw it
(a body's bone, a door's leaf, a piece, a deployable, a tree, the ground); at most `arrows.most` (600) in a zone, the
oldest going first; `lifetimes.arrow` 300 s; not saved. When its holder goes (a tree felled, a box broken, a player
dead) it falls to the floor or ground below. Picked up with E (the pickup capability) whole into the slots, from the
living too. The server tells each player those in range (`ArrowStates` 211, in their holder's frame, again when one
falls; `ArrowsGone` 212); a client places each where its holder is every frame (`GameClient.Arrows`) and `ArrowsView`
draws them with the arrow's model as one multimesh (one draw call, no shadow, 512 within 60 m).

Tests (`InteractionTests`): every verb has exactly one handler; the one query meets the nearest thing; the garage door
shuts through its opening; one intent passes gates, reach and the offer; a fresh code lock sets its code first; the
cupboard authorises, then opens; deployables are picked up empty where one may build; spent arrows stick, ride and are
taken back; a client aims and offers as the server. The old intents' tests use `Interact`, asserting the same.

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

*As built* (package P3, `ground-p3`; GROUND.md §11, decisions V1 on):

- **Parts.** `ItemPart` (`Content/ItemParts.cs`) names the 21 blocks of data that give an item behaviour: melee, bow,
  arrow, consumable, wearable, fuel, cookable, deployable, ship, firearm, round, attachment, throwable, plan, hammer,
  cupboard, lock, key, door, blueprint and component (an ingredient of a recipe, or recycled into salvage). One table
  (`ItemPartTable`) says where each is declared and which held behaviour it gives; when the data has been read, every
  item gets its parts (`ItemDef.Parts`), what uses it (`UsedIn`) and its one held behaviour (`ItemDef.Held`, a `HeldUse`,
  and `HeldPart`). Two parts that would each give one are a problem in the data. Test:
  `HeldTests.HeldBehaviourComesFromOnePart`.
- **Held behaviours on the server** (`Zone.Held.cs`): one handler per `HeldUse` in a registry the zone seals at start.
  The tick asks the item in hand's handler for its buttons (melee swings, the bow drawn and loosed) and, when the hands
  change, the handler of what was put away ends what was going on; the rest act by intents. An intent a held behaviour
  sends is checked by that behaviour (`Zone.ItemFor`): a throw of something whose held behaviour isn't `Throwable` is
  refused, whatever it is.
- **Held behaviours on the client** (`game/scripts/Ground/Held/`): one control per `HeldUse` (`IHeldControl`) in a
  registry the game builds at start (complete, or it doesn't start; `--ui-check` checks it). The ground view asks the item
  in hand's control what the attack and aim buttons and R do this frame (`Hands`), which radial a held input opens
  (`Radial`: the aim button's at once, the plan's pieces and the hammer's work; R's after 0.35 s, a gun's rounds), the
  HUD's ammo counter (`Ammo`), where a placeable's ghost goes (`Aim`) and what the first-person arms do (`Arms`: a melee
  swing in the server's rhythm, a bow's draw, a gun's attachments). The view's `Hands` switch, its `Throw` and
  `ItemDef.Hands` (`HandUse`) are gone, and so are `FirstPersonView`'s melee and bow branches and its placeholder stick
  (every item held in sight has a model; a test checks it). Free look, death and the pilot's seat stop the hands in one
  place, the view's `HandsFree`, before any control is asked, so no radial of the item opens while Alt is held either.
  The controls: melee and the bow send the tick's buttons; the firearm sends them too (the predicted gun fires from them),
  and R opens its rounds (`AmmoRadial`: every round of its calibre carried with how many, then UNLOAD; the current option
  the round a tap reloads with, the loaded kind while there are more; with none carried the loaded kind shows refused, so
  a tap says why); a throwable throws, puts a charge on a surface or lobs a grenade; a placer and the planner place and turn
  through `IPlacement`, P2's system (today `TodaysPlacement` sends each placeable to its old entry point by the part that
  makes it placeable: a deployable to `LocalPlayer.Place` with its ghost, the plan's piece, doors, locks and the cupboard to
  `BuildView.Use` and `Rotate`; a ship says it can't be placed yet); the hammer repairs and opens its radial; food,
  medicine and blueprints do their part's first inventory button (EAT, USE, LEARN). The model catalogue says what was
  picked by item key: `held.flame` (the torch's fire, `fire.torch`), `held.pose` (`pistol`, `rifle`: the arms' pose and
  the muzzle's place when a model has none) and `held.eyeRelief` (the rifle's). Test:
  `HeldTests.HeldItemsShowFromTheCatalogue`.
- **Information** (`Items/ItemRows.cs`): a describer per part in a registry (every part has one, or nothing starts), each
  adding its rows; an item's information is its parts' rows in order, then its condition. A stack is described with
  what is its own: a gun's magazine and attachments (which change its numbers), what a blueprint teaches and whether the
  reader knows it. Guns say their damage, fire (auto or semi, rounds a minute), magazine, reload, aim cone hip and aimed,
  range and recoil; rounds what fires them, their damage, speed, bleeding, fire and blast; attachments what they fit and
  change; explosives their fuse and duds, blast, and how many break each grade of wall and each door; deployables their
  health, slots, workbench level, fuel, cooking, warmth, machine and respawn time; doors, locks, the cupboard, the plan
  and the hammer their numbers from building.json; ships their hull, fuel and speeds; components what uses them and what
  they recycle into; and melee (now with structures and bleeding), bows, arrows, food (now with poison), clothing (now
  where it is worn), fuel and cooking as before. The panel lays the rows out label left, value right, and narrows a long
  value's font until it fits (`IGroundUi.Rows`). Test: `HeldTests.EveryPartIsDescribed`.
- **Inventory actions** (`Items/ItemActions.cs`): items.json's `actions` list (use, eat, wear, unequip, learn, unload,
  remove; append only, a place is the wire number, a label may name the item: `REMOVE {item}`) and one handler per
  action, listed under the part that offers it (`IItemAction`: `Offer` the buttons of it a stack has, enabled or with the
  refusal; `Do` it). A stack offers what its parts' handlers offer: food EAT and medicine USE, clothing WEAR (refused,
  `Worn`, while something worn has its layer) and UNEQUIP, a blueprint LEARN (refused once known), a gun UNLOAD (refused
  while empty or reloading) and REMOVE for each attachment. One `ItemActionIntent { Seq, From, Slot, Expected, Action,
  Arg }` (message 230) sends any of them; the server (`Zone.ItemAction`) does an action only when its handler offers it on
  that stack, enabled, and the client predicts it with the same handlers (`LocalPlayer.ItemAction`; `IItemActor` is the
  player as each side has them: eating's vitals and a learnt blueprint's count are the server's). `UseIntent` (53) and
  `DetachIntent` (65) are gone, with `Zone.UseItem`, `Zone.Detach`, `LocalPlayer.Use` and `Detach`, the panel's
  `MoreActions` and its USE and WEAR code. DROP and the split stay the inventory's own. The panel shows what
  `IGroundUi.Actions` gives, then DROP; a refused button is muted and a click on it says why (as a radial's disabled
  option). A click with food, medicine or a blueprint in hand does its part's first button (`LocalPlayer.UsePart`). Test:
  `HeldTests.InventoryActionsComeFromParts`.
- **Guns.** UNLOAD returns the rounds to the inventory, all or (no room) none; the kind stays named, so R loads it again.
  The gun in hand's goes on its own timeline (`UnloadIntent`, message 231, a `GunActionKind.Unload` in order with its
  shots and reloads, predicted by `LocalGun.Unload`); a gun in the main slots or elsewhere on the belt is unloaded by the
  item action. A gun's slot shows its loaded round's icon at the bottom left (`ItemStack.Round` in the interface's model),
  the count staying at the bottom right, on the belt, in the inventory and in loot. Test: `HeldTests.UnloadReturnsTheRounds`.
- **Dropping**: a drag released on another slot moves there, on its own slot is put back, and anywhere else (over a
  panel, a button or the world) drops (Rust). `--ui-check` checks the panel and the own slot.
- **Workbench speed** (`Items/CraftingSpeed.cs`): a job takes its recipe's time times recipes.json's `workbenchSpeed`
  factor for how many levels the best workbench in reach is above the recipe's (1, ½, ¼: Rust's; the last holds for
  more), fixed in the job when it starts (it keeps that time if the bench goes); a station's own recipe (the Tin Finch)
  keeps its time. The server, the client's prediction and the crafting panel use the one rule: the panel's clock shows the
  time a unit takes here, in green when shorter, and the workbench line says how much faster (`IGroundUi.SecondsHere`);
  the queue's progress follows each job's own time. Test: `HeldTests.AWorkbenchAboveTheRecipeCraftsFaster`.
- **The gun's answers** joined the one answer list (§3.2): `GunResult` is gone; its refusals are `OutcomeCode`s with
  lines in texts.json (`NoWeapon`, `Reloading`, `Empty`, `Cadence`, `NoAmmo`, `Full`, `Lowered`, `TooLate`, numbered
  from 100), and `GunUpdate` carries the code in 7 bits.
- **Cost:** the zone's tick does one array lookup per player for the held behaviour; the client one per frame for the
  control; the item panel's rows and buttons are worked out once per stack and cached until it, the inventory, the
  blueprints or the gun change; the ammo radial lists the calibre's rounds only while open.
- **Pictures** (a hosted game, `--tour-steps held`): `docs/images/ground/held/ak-info.jpg`, `bag-charge-info.jpg`,
  `ammo-icon.jpg`, `ammo-radial.jpg`, `blueprint-learn.jpg`, `crafting-workbench.jpg`. Protocol 17; six tests.

## 8. Damage and destruction (package P4, built: GROUND.md §6.4.2)

One damage request (who, with what, how delivered, the damage channels, amount, body region, point, direction); a
registry of receivers by target family, each with its own defences and consequences; every delivery (melee, arrow,
bullet, blast) calls one entry point; one consequence step emits **Destroyed** `{ Target, model key, pose, direction of
the blow, cause }` to everyone near. Gibs, effects, sounds and support links listen to it. Built by `ground-damage`
(GROUND.md §6.4.2, decisions D6–D11): each receiver finds its own things along a ray and in a blast's reach; since P0
they are registered by target family and name what they hit by `TargetRef` (§3.1).

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
2. **P0** foundations (built 13 October 2026), then **P5** the radial.
3. **P1** interaction (built 13 October 2026), **P2** placement and **P3** held items, in parallel.
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
