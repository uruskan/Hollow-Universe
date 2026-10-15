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

### 3.8 Queries through ships' frames (IW1, IRON_WAKE.md §4.9)

A ship people walk in has its own physics (its space) in its own frame, so a query of the zone's frame alone would pass
through its hull. Every query that meets the world also enters each ship space its segment or sphere crosses, through one
shared helper, `ShipQueries` (`Ray`, `Clear`, `Nearest`; the bounding sphere first, then the ray turned into the ship's
frame): the aim's backdrop (`ShipTargets.Aim`, and the client's through the ships as drawn), reach and sight
(`TargetSystem.InSight`: a ship's piece hides what is behind it), a blast's line of sight (`BlastSystem.Visible`) and the
damage pipeline's bullets and arrows (`ShipReceiver.Raycast`, the ship where the pose history had it for a late shot).
What a character aboard does in the world asks `CharacterWorld` for where its ship carries it (`WorldOf`, `UpOf`,
`VelocityOf`, `AttackLine`): its feet, its eye where it saw its ship, and the velocity a thrown or shot thing keeps.

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
| Ship (by part) | Hatch, Board, Refuel, Cargo, Lock. Since IW2 (IRON_WAKE.md §5.7) each part's own, by its capabilities in `ships.json`: a seat (`seat`: board, take the helm, man, sit, the seat's name in the prompt), a door (`ship-door`: open, close, lock, unlock), the tank (`fuel`: refuel), the cargo (`container`), the helm's list (`cupboard`) |

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
key lock, code lock, cupboard) and `Interactions.World.cs` (wounded: help up; character: invite; dud: light; seat: board,
take the helm, man, sit; ship door: open, close, lock, unlock; fuel: refuel; arrow: falls when its holder goes). Each names its verbs by key and finds what one does in a table
by number (`VerbActions`), never by testing which it was given. `InteractionRules` gives every verb its one handler in a
sealed registry (a verb without one, one with two, or a handler's verb the data lacks stop the start), and makes a
target's options: every capability's offer, each verb's gates, ranked by priority; `Primary` is the first primary that
can be done, else the first primary (the tap says why), else none. New capabilities: `pickup` (gather.json's pickups
and bushes, bags, arrows), `portable` (deployables.json `pickup`, on all eleven), `station` (`workbench`), `wounded`,
`character`, `dud`, `arrow` (after P2's `standing`). New answers: `Seated` and `NotEmpty` (78, 79).

*What a handler knows:* `TargetFacts` (`Targets/Targeting.cs`): its capabilities and viewer flags, name, count, whether it is
a person's (looted, not opened), open, busy, at rest, lock and definition, from its family (`TryFacts`) on the server and from the replicas on a client
(`TargetModule`, one view per family), so `Offer` runs the same on both. `InteractContext` adds the doer
(`Doer`: dead, asleep, wounded, seated; in a team, its leader, a full team) and the building (rights at a place), and on
the server the zone, the player and the thing.

*Targeting:* every family is an `ITargetView` (`TryPose`, `TryNearest`, `Aim`, `TryFacts`; `ITargetFamily` extends it), and
`Targeting.Aim(views, ray)` keeps the nearest hit of all, as far as the longest verb reaches, past the one looking. The
shapes are shared so both sides meet the same thing (`AimShapes`: boxes, balls for bags and bodies, capsules for arrows;
`DoorShapes`, a door's leaves and its opening; `ShipShapes`, the hull and its parts; `NaturalAim`, the natural objects
within a sphere round the ray; `BuildingAim`, one building ray; the ground by P2's `GroundProbe`, shared with placing). A hit says what it is to the view (`AimKind`): something to act on, a backdrop (the ground,
pieces, trees and rocks, animals: they stop the view and offer nothing) or an open door's opening, through which the view
goes on: the door is met there only when nothing beyond is to act on. `Zone.Aim`, `GameClient.Aim` and
`GameClient.AimInteraction` (the target, its facts, its options within each verb's reach, the primary: the prompt's and
the radial's), 23 µs a frame in the test's fan of views. Since S3 (14 October 2026) a zone is a whole world in its own
frame (ARCHITECTURE.md §6, §10.1), so these queries are in three dimensions: natural objects are found by a sphere
(`INaturalObjects.Query(centre, radius, into)`), for the aim as for the damage pipeline's rays and the placement world's,
and deployables, the building's pieces, natural objects' changes, animals and relevance are indexed by cubes with
straight-line distances.

*The intent:* `InteractIntent` (210) `{ Seq, Target, Verb, Arg, Code }`, sent by `LocalPlayer.Interact(target, verb, arg,
code)`; on the server `Zone.Interact` (`Zone.Interaction.cs`): a verb it knows, the gates, a panel's verb refused (the
interface's own), the target, `Zone.Reach` with the verb's reach and sight, the offer holding the verb enabled (else
`NotUsable`, or the offer's reason), the handler's `Do`; answered by `IntentResult`. The tech tree's unlock is the
station's `unlock` with the recipe in `Arg`, sent to the best bench in reach (`GameClient.BenchNear`). Deleted: messages
56 (Open), 58 (PickUp), 60 (Toggle), 66 (HelpUp), 112 (Rearm), 130 (Unlock); `TeamAction.Invite`; `ShipAction.Board` and
`Refuel` (the seat's hatch, exit, lock and descend stay); the door, lock and claim intents (92–94: P2 took their
placing); the zone's `Open`, `PickUpNatural`, `PickUpItem`, `Toggle`, `Rearm`, `Board`, `RefuelShip`
and ships.json's `reach`; the client's `Interaction`, `InteractionKind`, `ShipInteraction`, `AimShip`, `AimAtDuds` and the
intents' methods. Protocol 19 (P3 took 17, P2 18).

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

**IW2 (15 October 2026, IRON_WAKE.md §5.7).** A ship's parts are its data's. The capabilities `seat`, `ship-door` and
`fuel` are appended to `capabilities.json`, and the verbs `take-helm`, `man` and `sit` to `interactions.json`. The
old `ship` capability keeps no verbs: only its flags (locked, on the list).
- An option may name what it acts on (`InteractOption.Name`: "MAN FORWARD STARBOARD MOUNT", "CLOSE RAMP"), and the
  prompt says it.
- The cupboard's verbs act on the list of the target's family (`IAuthoriseFamily`: a claim device's, a ship's), so the
  Iron Wake's helm is its tool cupboard with no second handler.
- The hammer mends whatever the aim names through its family (`IRepairFamily`: a piece, a ship's door or battery;
  `BuildingSystem.Repair`), within the building's reach and in sight.

Tests (`InteractionTests`): every verb has exactly one handler; the one query meets the nearest thing; the garage door
shuts through its opening; one intent passes gates, reach and the offer; a fresh code lock sets its code first; the
cupboard authorises, then opens; deployables are picked up empty where one may build; spent arrows stick, ride and are
taken back; a client aims and offers as the server. The old intents' tests use `Interact`, asserting the same.

## 6. Placement (package P2, built 13 October 2026: GROUND.md §6.10, decisions Z1–Z12)

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

*As built:* `game/data/ground/placement.json` (hashed): `defaults`, one block per kind (`deployable`, `piece`, `door`,
`lock`, `cupboard`, `charge`, `ship`) and the append-only `placeables` list, whose index is the wire's number: 42
entries (11 deployables, 20 building pieces, 5 doors, 2 locks, the claim device, 2 charges, the Tin Finch). A
placeable's rule (`PlacementRule`) is the defaults, its kind's block over them and the `placement` block next to its
definition over that (deployables.json, building.json's pieces, doors, locks and claim, explosives.json's throwables,
ships.json), read by one reader (`Content/PlacementReader.cs`). Beyond the table: `supportStep` (15 cm, RustMVP's),
`turn` (R's step), `sight` (the slack the sight ray may end short), `padding` (claims reaching further, the building's
2.25 m) and `tolerance` (0.3 m). `pieceSurfaces` names what each family of piece offers what stands on it: foundations,
floors and stairs a `floor`, walls a `wall`, roofs a `roof`. Every item that places something knows its placeable
(`ItemDef.Places`); a piece has the building plan as its tool and its twig as its price. The content check refuses a
placeable that names nothing, a deployable, door, lock, charge or ship left out of the list, and a name without its
handler (one problem each, not one per placeable).

**Solvers and checks** are registered by name. A solver turns the aim into a candidate (pose, support, socket); a fixed
pipeline of data-configured checks (reach, sight, surface and support, slope and step, water, overlap, privilege,
limits) gives the verdict and its reason; the building solver adds its own checks (stability, sockets). The client runs
it every frame for the ghost against its replicated world (terrain, the building mirror, things' boxes, characters,
claims); the server runs it again and accepts only a pose within a small tolerance of its own (RustMVP's rule).

*As built:* `src/Hollow.Simulation/Placement/`. **Solvers** (`Solvers.cs`): `free` (the first body the aim meets, then
straight down from a little above it to what it would stand on within the step; under a ceiling, from just under it),
`socket` (the building's own scoring of every compatible socket near the aim, or a free foundation), `opening` (the
doorway or wall frame the aim passes through), `attach` (the door aimed at, at its lock spot) and `sweep` (the
explosives' own sweep to the first surface). **Rotations** (`Rotations.cs`): `free` (the view's yaw plus R's turns; since
S3 a placement's yaw is a heading from local north, `Facing`),
`steps`, `support` (square to the piece under it, in its 30° steps), `flip` (a door's side) and `fixed` (as the solver
found it: a piece's orientation on its socket, which R cycles, or a lock's spot). **Checks** (`Checks.cs`), in
placement.json's order: reach (to the footprint's nearest point), surface, step, slope, water, overlap, support (every
corner within 15 cm of an allowed surface facing up), sight, privilege, limits, and `own` (the kind's: a piece's
building checks, a door's opening, one lock to a door, the claim device on a slab). **Kinds** (`Kinds.cs`) say what a
key names in the data, their own rules, how a candidate is drawn turned, what counts against a limit, and, on the
server, what placing one makes (`Put` through `IPlacementSite`: the zone, or the build sandbox's building alone).
**Footprints** (`Footprint.cs`): a box lifted 2 cm (a deployable's or the claim device's size), a ship's hull (its boxes
from ships.json, through `ShipCollider`), or a point. All four lists are sealed registries (`PlacementHandlers`) keyed
by placement.json's lists. The pipeline is `Placer`: `Plan` (the client's ghost: each candidate through the checks, the
first that passes, else the best with its reason), `Settle` (the server: the solver works the claim out again, takes it
only within the rule's tolerance of its own, then the checks) and `Check`. Both run over a `PlacementWorld`: the zone's
(`Zone.Placement`) or a client's (`GameClient.Placement`: its prediction physics with the ground, trees, rocks and solid
deployables, its building mirror, the things, characters and ships it was sent, what stands on what), whose rays name
what they meet by its owner (`TargetRef`) and the surface it offers. Reach, sight and building rights moved out of the
building's rules into the pipeline; the building keeps stability, sockets, collisions and the price. Tests:
`PlacementTests` (nine, one a rule).

**One ghost:** the thing's catalogue model in a hologram material, one colour when it fits and red when not, with the
reason under the crosshair (the building plan's line), and a pending look until the server's answer arrives. R turns it
by its rule. Building pieces keep their batched ghost mesh in the same material.

*As built:* `game/scripts/Ground/PlacementView.cs` is the one controller, behind the held controls' `IPlacement` (§7:
the placer's, the planner's and a charge's), whatever is held: every frame the held item's control aims it and it asks
`GameClient.Plan` for the item's placeable (or the plan's piece), a click sends what the ghost shows (`Local.Place`), R
turns it; with nothing aimed in a frame there is no ghost. `PlacementGhost` draws it in one hologram material
(`shaders/placement_ghost.gdshader`: unshaded, divided by the camera's exposure, a rim and faint lines), pale blue when
it fits and red when not, the line under the crosshair saying why (`BuildHud`); its model comes by the kind's name (a
piece's kit mesh, a door's frame and leaves), else the model catalogue's by the placeable's key (deployables, locks, the
claim device, charges, the Tin Finch). Sent placements are drawn breathing in a pale colour (`Local.PendingPlacements`)
until the answer, half a second more once accepted. The build sandbox uses the same ghost and pipeline. `BuildGhost` and
the hand's ground aim (`AimGround`) are gone.

**Wire:** one `PlaceIntent { Seq, From, Slot, Expected, Placeable, Support, Socket, Pose, Variant }`, replacing
PlaceIntent, PlacePieceIntent, DoorIntent.Place, LockIntent.Attach, ClaimIntent.Place and PlaceChargeIntent. Throwing
stays its own action and keeps the shared sweep for sticking.

*As built:* message 220 (protocol 18): the slot that pays and the item seen there, the placeable's number, the support
(`TargetNet`), the socket's code, the pose (`PoseNet`: a point in the zone frame, the world's own since S3, and a 16-bit
yaw from local north) and the variant (a piece's orientation or a free foundation's heading, a door's side, a turn in the
support's steps), answered by
`IntentResult`. Messages 59, 90 and 111 are free; `DoorIntent`, `LockIntent` and `ClaimIntent` lost their placing
actions (and, with P1's verbs, everything else: 92–94 are free too, §5). The server's handler (`PlacementModule`) is one call, `Zone.Place(entity, PlaceRequest)`: the slot must
hold the placeable's item (or its tool), the solver settles the claim, every check passes, the kind puts it, then the
item or the materials are taken. The client takes the price at once and rolls it back on a refusal.

**Support links:** what stands on a piece is linked to it, and goes when the piece goes (Rust).

*As built:* a deployable placed on a piece keeps the piece as its `Support` and is held by it (§3.6); the `standing`
capability (every deployable) breaks it when the piece goes, its contents spilling in one bag. The link is saved (the
thing record's version 4: no SQL schema step) and settled once the building is back after a restart; a thing whose piece
didn't come back breaks. `ThingSpawn` carries it, so a client counts what stands on a structure as the server does.
Doors and locks are the building's parts; charges already were (§3.6).

**Fixes:** furnaces, boxes, campfires and bags stand on foundations and floors; every placeable has a ghost with a
verdict, the garage door, locks, the cupboard, charges and the Tin Finch included; one reach, sight, water and privilege
rule; the Tin Finch is placed from its item (`ships.json` gets a placement rule with the hull as its footprint, and the
ship family's placement handler calls `SpawnShip`).

*As built:* all of them (the pictures in GROUND.md §6.10). A deployable stands on the ground or the top of a foundation,
floor or stairs' landing (not a wall, a roof or another thing), every corner held; the claim device on a slab, one to a
structure. The Tin Finch's item places it within 12 m, its skids on the ground or a slab's top no steeper than 20°,
dry, its hull clear, with ships.json's `fuel.placed` (50 of lamp oil, its recipe's first tank); the vehicle workbench
still rolls a built one out onto its bay. The Tin Finch's item is held as a placer (its ship part's held behaviour,
§7), so in the belt it shows its ghost and the attack button places it.

**Cost:** client solve and ghost under 0.2 ms a frame.

*As built:* measured by `PlacementTests.AThingGoesWithTheFoundationItStandsOn` on the loopback client: 32 µs a frame for
a furnace's ghost, 80 µs for a wall's on a foundation's sockets; a refused piece tries up to eight sockets with the
building's full checks (about 0.45 ms, as before P2). The server's verdict is one settle and the same checks per intent.

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
  through `IPlacement`, P2's system (since P2 merged, `PlacementView`, §6: the ghost of whatever is held, a charge's too,
  and one `PlaceIntent`); the hammer repairs and opens its radial; food,
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
   request (P4); tree X and node hotspots; loot bags (all merged 13 October 2026); gibs. The Tin Finch's placement came
   with P2.
2. **P0** foundations (built 13 October 2026), then **P5** the radial.
3. **P1** interaction (built 13 October 2026), **P2** placement (built 13 October 2026) and **P3** held items, in parallel.
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

## 12. The kernel: systems, events, messages, modules and readers (packages K0–K4, 14 October 2026)

### 12.1 Why

The zone, the server, the client and the content loader each grew as one class cut into partial files: `Zone` was 24
files and 8,800 lines whose parts called each other freely (the players' file reached into 20 others), `GameServer` 12
files, `GameClient` 14, and `GameContent` carried a 1,200-line reader inside. A feature added about 1,400 lines to
`Zone`, a field to the one event struct and a case to five switches; 97 of the branch's 419 commits touched those four
places, so every parallel package collided there. With most of the game still to come, that was the shape that would
not scale. The owner decided (14 October 2026): one dedicated server for now, so no zone host and no transfers yet
(ARCHITECTURE.md §10.1 and §12 stay the plan), but the rest is fixed now, by applying §2's rule one level up: registries
for the tick, the events, the messages, the server's and the client's concerns, and the data files.

### 12.2 K0: the zone is a host; the rules are systems

Decisions:

- **K1.** `ZoneSystem` (`src/Hollow.Simulation/Systems/ZoneSystem.cs`): one class per concern, with its own state,
  `Init` (finds what it needs), `Step` (its tick: every tick, or once a second for the slow ones), `Dispose`. What a system
  holds is its own; C# enforces it (private is private), so nothing reaches into another system's state.
- **K2.** `Zone` keeps only what every system shares: the spec, the surface and frame, the entities, the physics, the
  natural objects, the pose history, the tick and the clock, the content, the events, the climate, and its list of
  systems. Its step: the ground under the characters; then, under the physics gate, the broadphase, the characters'
  movement and the systems that move bodies (`InsidePhysicsGate`); the ground's release; then the other systems in their
  order. The host times each system (`ZoneSystem.Time`, principle 6).
- **K3.** The list and its order live in one place, `GroundSystems` (`Systems/GroundSystems.cs`), in GROUND.md §4.2's
  order; a movement-only zone (G1's, the tests') has `AutoRespawn` alone. A zone is made from a list, so a later kind of
  zone (space) is the same host with other systems.
- **K4.** Systems find each other once, in `Init`, with `zone.Get<T>()` (null when absent) or `Require<T>()` (fails at
  start naming what is missing). Each system's file gives the zone an accessor (`zone.Players()`, `zone.Guns()`...), an
  extension method, so the host never names a system and a caller never spells `Require`.
- **K5.** No `Content == null` paths: a system exists only in a zone with content, and `ZoneSystem.Content` is never
  null. The 68 checks go.
- **K6.** The check (`ArchitectureTests.TheZoneIsAHost`): `Zone` has no partial files beyond the host; every class
  deriving `ZoneSystem` is in `GroundSystems` exactly once (completeness, as §3.5's registries).

| System | Was | Its step |
|---|---|---|
| `PlayerSystem` | Zone.Players.cs, Zone.Admin.cs | fall damage; lives, sleep, death, respawn; teleport, god and noclip |
| `CombatSystem` | Zone.Combat.cs | held items' actions (swings, bows), arrows in flight, hits on characters, natural things and the ground |
| `GunSystem` | Zone.Guns.cs | gun actions and bullets |
| `ExplosiveSystem` | Zone.Raid.cs | thrown and placed explosives, fuses, detonations |
| `BlastSystem` | Zone.Blast.cs | an explosion's reach and line of sight, raid tallies |
| `WoundedSystem` | Zone.Wounded.cs | the wounded getting up or dying |
| `AnimalSystem` | Zone.Animals.cs | the animals' AI, attacks, harvest |
| `VitalsSystem` | Zone.Vitals.cs | metabolism, temperature, bleeding (once a second) |
| `NatureSystem` | Zone.Players.cs (part) | the natural objects' respawn (once a second) |
| `ThingSystem` | Zone.Things.cs | static things; lifetimes (once a second); burners and machines |
| `ArrowSystem` | Zone.Arrows.cs | spent arrows, their poses (once a second) |
| `WorldSystem` | Zone.World.cs | props, loot spots, recyclers and research, teams, map markers (once a second) |
| `ActionSystem` | Zone.Actions.cs, Zone.Players.cs (part) | containers, looting, item actions; the loot check (once a second); craft queues |
| `BuildingSystem` | Zone.Building.cs | building, doors, locks, the cupboard; decay and upkeep |
| `HistorySystem` | Zone.Players.cs (part) | records the second of poses hits are judged against (last) |
| `ShipSystem` | Zone.Ships.cs | ships (inside the physics gate, after movement) |
| `DamageSystem` | Zone.Damage.cs | the damage pipeline (no step) |
| `SpillSystem` | Zone.Spill.cs | dropped items in bags (no step) |
| `SupportSystem` | Zone.Supports.cs | support links (no step) |
| `TargetSystem` | Zone.Targets.cs, Zone.Reach.cs | families, capabilities, viewer flags, reach and sight (no step) |
| `InteractionSystem` | Zone.Interaction.cs | verbs, options, aim (no step) |
| `PlacementSystem` | Zone.Placement.cs | placing (no step) |
| `HeldSystem` | Zone.Held.cs | what a held item does (no step) |
| `AutoRespawnSystem` | Zone.cs | G1: the dead come back by themselves (movement-only zones) |

### 12.3 K1: events are types

- **K7.** One record struct per kind of event (`Events/`), each an `IZoneEvent`. `Zone.Events` keeps them in the order
  they happened and by type: a consumer subscribes with `On<T>(handler)` and the host dispatches after each step; the
  lists are read (`Of<T>()`) and cleared by whoever consumes them (the server; the tests look at them), as today.
- **K8.** The union struct `ZoneEvent` and `ZoneEventKind` go. The server's 23-case route and its counters, and the
  animals' hearing, become subscriptions.

### 12.4 K2: messages find their handler

- **K9.** Every generated message implements `INetMessage<T>` (its id, its reader, its writer).
  `MessageHandlers<TContext>` holds one handler per message type (a second throws) and routes a packet's type byte to
  it without allocating; a type with no handler, or a read that fails, is a bad packet, as today. Routing by type stays
  transport (principle 4); the switches and the chain across five server files and eight client files go (K3).

### 12.5 K3: the server and the client are modules

- **K10.** `ServerModule`: one class per concern with its own state; `Init` registers its message handlers and event
  subscriptions; the server calls its hooks in order (`AfterStep`, `Prepare`, `Replicate(session)`, `Finish`,
  `Joined(session)`, `Left(session)`, `Save`, `Restore`); per-session state lives in typed slots (`session.State<T>()`),
  replacing the per-feature properties and weak tables. `GameServer` keeps the connection, logins, the tick loop, the
  snapshots of dynamic entities, saving and the console's plumbing.
- **K11.** `ClientModule` likewise for `GameClient` (`Init`, `Update`, `Welcome`, `Dispose`); the presentation reads a
  module's state through its accessor (`client.Raid().Explosives`).

### 12.6 K4: one reader per data file

- **K12.** `IContentReader`: the file it reads, `Read` and `Check`; `ContentReaders.All` in reading order (items before
  what refers to them); the reader inside `GameContent` goes, and the validation goes with the file it checks (the
  cross-file checks last). The hash, the problems and every public member of `GameContent` stay.

### 12.7 Order, guards, what stays

- **K13.** K0, then K1; K2 and K4 beside them; then K3, the server and the client in parallel. Each lands with every
  existing path moved and the old one deleted, the 218 tests green and one check of its own; the protocol is unchanged
  unless a layout changes.
- **K14.** Deferred by the owner (14 October 2026): the zone host, transfers, the lifecycle with catch-up
  (ARCHITECTURE.md §10.1, §10.4, §12) and P6 (§9). Nothing here has to be redone for them: a second zone is a second
  host with the same lists, and P6's one entity model sits inside the systems.

*As built, K0 (14 October 2026):* `src/Hollow.Simulation/Systems/`: `ZoneSystem` (the base: `Init`, `Start`, `Step`, `Every`,
`InitOrder`, `InsidePhysicsGate`, `Time`, `Dispose`, and the host's shared things under the names the rules use),
`GroundSystems` (the lists: `Game`, and `MovementOnly` with `AutoRespawn`, `Ships` and `Damage`, since ships crash and break
in G1's zone too), and one class per row of the table above; `Zone.cs` is 360 lines. The host runs `Init` in `InitOrder`
(the things, the building, the placement, the world, the animals, then the targets, the damage receivers, the held rules
and the interaction rules, as the old `InitGame` did) and steps in the list's order. Each system's file ends with its
accessor (`zone.Players()`); a system keeps the others it needs in lazily found fields. Three overloads that clashed
across files were renamed: the supports' `TryLinkPose`, the actions' `ResolveContainer`, the explosives' `DropExplosive`.
A system's world object is its `World` (`zone.Building().World`), its main collection its `All` (`zone.Ships().All`). What
was private and used by another system is internal (154 members), to be narrowed as P6 settles the entity model.
`SetCheats` and the ground helpers (`Settle`, `HeightAboveGround`, `GroundNormal`, `EyeOf`) are the host's. The conversion
was scripted (every member's new home chosen by its file; 575 call sites outside the simulation moved), so the rules'
text is the same, line for line, where it stands. Test: `ArchitectureTests.TheZoneIsAHost`.

*As built, K2 (14 October 2026):* `src/Hollow.Net/Protocol/INetMessage.cs` and `MessageHandlers.cs`; the generator
(`tools/Hollow.NetGen/NetGenerator.Emit.cs`, `EmitStruct`) gives every `[NetMessage]` struct the interface. The schema hash
is unchanged. Test: `NetTests.MessageHandlersRouteByTypeByte`.

*As built, K4 (14 October 2026):* `src/Hollow.Simulation/Content/Readers/`: `IContentReader` with `ContentReading` and
`ContentChecking`, nineteen readers in `ContentReaders.All` (one per data file, `CrossChecks` last), `ReaderHelpers`;
`GameContent.cs` went from 1,675 lines to 400 and `Validation.cs` is gone; the content hash and every problem text are
unchanged. Test: `ContentTests.EveryDataFileHasOneReader`.

*As built, K1 (14 October 2026):* `src/Hollow.Simulation/ZoneEvents.cs` (`IZoneEvent`, `ZoneEventHandler<T>` and
`ZoneEvents`: `Add`, `On`, `Of`, `Count`, `Clear`) and `Events/` with the twelve records: `HitConfirmed`, `Hurt`,
`ItemsGained`, `Crafted`, `Died`, `ProjectileLaunched`, `ProjectileEnded`, `WentDown`, `GotUp`, `Detonated`, `Notice`,
`Destroyed` (named so that none clashes with a message). A subscriber hears an event the moment it is added, on the tick
thread; the lists keep the tick's events until the server clears them after the step. The server subscribes once at
start (`GameServer.RouteEvents` and `GameCounters.Listen`); the animals' hearing subscribes in `AnimalSystem.Init`. The
union struct, its kind enum, the 23-case route and the animals' scan of the list are gone; the 25 producers name their
fields. Test: `EventTests.EventsAreTypedAndHeard`.

*As built, K3a (14 October 2026):* `src/Hollow.Server/ServerModule.cs` (the base: the hooks `Init`, `Restore`, `Start`,
`Joined`, `Left`, `BeforeStep`, `Input`, `AfterStep`, `Prepare`, `Describe`, `AtRest`, `Seen`, `Snapshot`, `Replicate`,
`Finish`, `Save`, `Dispose`, and the host's shared things and helpers under the names the code uses: the zone, the content,
the options, the writer, `Send`, `SendNear`, `Notify`, `Intent`, `Present`...), `ServerModules.cs` (the one list, in the
order the hooks run) and `Modules/`, one class per concern: `ThingModule` (static things and natural objects: replication
by relevance, save and load), `BuildingModule`, `PlacementModule`, `ShipModule`, `RaidModule` (with the notification
service and the raid arena), `WorldModule`, `AnimalModule`, `ArrowModule`, `DamageModule`, `SurvivalModule` (the inventory,
crafting, interaction, close and respawn intents; the player's own state; the looks; the event routes; the counters),
`GunModule`, `ConsoleModule` (the F1 console's rights, messages, clock and commands; `AdminCommands.cs`, the commands'
switch, stays the host's and calls them through the accessors), `DevModule` (the F2 panel, GROUND.md §8.5: who watches,
the stats every half second, a target described, the nav grid and the spawn areas around a watcher, the log). Each
module's file ends with its accessor
(`server.Raid()`, `server.Ships()`...); the host finds a module by type (`Get<T>`, `Require<T>`). The list's order is what
a client receives in a tick, unchanged: after the step the ships' notices, the raid alerts, the world's messages, then
the player's own state and the intents' answers; in a snapshot the things and natural objects, the building, the
explosives, then the arrows. One order serves both ends only with the things' replication as a module of its own, not
the survival module's, so it is. The host registers its own messages (`InputPacket`, `ChatSend`) and dispatches every
other type through K2's table; a type nobody handles is a bad packet, as before. Animals enter the snapshots through
`Present`, and their presenter answers `Describe` (the Spawn) and `AtRest`; the looks go through `Seen`; the ships fill
the header and the candidates through `Snapshot`. Per-session state moved off `PlayerSession` into typed slots
(`session.State<T>()`): what each module sent a player (things and nature cells, the building view, explosives, arrows,
ships, the world view, the own state's revisions, the looks), the gun budget (`GunBudget`, public: the load test reads it)
and the console's; the session keeps what the host and several modules share (the connection, the inputs, the snapshots
and relevance, the chat and intent budgets with the answers, the viewer). `GameServer` was twelve files and 5,600 lines;
the host is `GameServer.cs` (1,079 lines) and `AdminCommands.cs`, with `NetPresence.cs` and `CharacterRecords.cs` (a
character's record and its survival state) beside them. The moved code is the old text, line for line, where it stands.
Test: `ArchitectureTests.TheServerIsAHost`.

*As built, K3b (14 October 2026):* `src/Hollow.Client/ClientModule.cs` (the base: `Init` with the handler table, `Welcome`,
`Spawned`, `Despawned`, `Seated`, `BeforePredict`, `AfterSnapshot`, `Sample`, `Render`, `Update`, `Dispose`, and the host's
shared things under the names the moved code uses), `ClientModules.cs` (the list, in the order the hooks run), and one
class per old partial in `Modules/`: `SurvivalModule` (the local player's state, things, natural objects, projectiles),
`TargetModule` (the families' views), `WorldModule` (props, the map, teams), `BuildingModule`, `PlacementModule`,
`InteractionModule`, `RaidModule`, `ShipModule` (the ship's prediction, through the hooks), `AnimalModule`, `ArrowModule`,
`ConsoleModule`, `DamageModule`, `LoadingModule`, `DevModule` (the F2 panel's reads and toggles, GROUND.md §8.5).
`GameClient.cs` is 837 lines (the core: the connection, the clock, the
input, the prediction, the snapshots, the remote entities, chat); it registers only its own messages (`Welcome`, `Spawn`,
`Despawn`, `ChatLine`; the snapshot keeps its special case) and dispatches every other packet through the table, where a
type nobody handles, or a message that doesn't read, is counted (`Stats.UnhandledMessages`) and dropped. On a Welcome the
host disposes the modules (what they made on the last zone's world), replaces the predictor, then calls `Welcome` in the
list's order; the core learns of a module only through a hook, never by name (the ship module says `Seated`, so the
pilot's controls ride in the input and the held item rests; it predicts the ship before the character, reconciles it
after a snapshot, samples and draws it). Each module's file ends with its accessor (`client.Raid()`); a module keeps the
others it needs in lazily found fields. Renamed, as the zone's systems were: a module's main collection is `All`
(`client.Animals().All`, `client.Ships().All`, `client.Arrows().All`), its world object `World` (`client.Building().World`,
`client.Placement().World`), the building's replica `Replica`; every other member keeps its name
(`client.Survival().ThingOf`, `client.Ships().Piloting`, `client.Interaction().AimInteraction`). The conversion moved 385
call sites outside the client (198 in the presentation, 42 in the bots, 145 in the tests) by a script from the compiler's
error list, so the moved code's text is the same where it stands. Test: `ArchitectureTests.TheClientIsAHost`.
