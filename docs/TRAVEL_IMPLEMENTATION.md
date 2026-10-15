# Connected travel implementation plan

This is an implementation proposal based on the current code, not completed gameplay. DESIGN.md TRAVEL-01/02
require the player to fly continuously from a surface through space to another planet, with no loading screen or
supercruise. LOCAL_FLIGHT.md records the smaller foundation already implemented. Its four-radius local boundary
remains until the following work replaces it with connected space.

## First complete scenario

Two connected players take a ship from Earth to one second landable body in the same system, land, gather, return,
and restart the server. One player pilots while the other walks on a carrier's decks; a deployed storage box and its
contents travel with them. The players retain their identities, equipment, inventories, seats or local deck positions,
and ownership. The same journey must work in a small ship and with two ships crossing at different times. A third
player remaining on Earth proves that travel does not replace the entire server's world.

Start with one process, one network connection per client, two planetary runtimes and one intervening space runtime.
That is a bounded multiplayer milestone. Interstellar layout, many active systems and world unloading follow after
the complete journey works. Avoid replacing travel with a menu selection, a teleport, or a parked-orbit flag.

## What can be retained, and what cannot be reused unchanged

| Existing component | Implication for travel |
| --- | --- |
| `ZoneSpec.ForBody`, `PlanetSurfaces`, `GameScale.PlanetRadius` | Already identify and build scaled planets; retain source planet generation. |
| `PlayableGalaxy.MapStar` | Its positions are abstract diagram units. Add a separate metric flight layout; never interpret these as metres. |
| `GameServer.Zone`, `Spec`, `Sky`, relevance grid and server modules | Currently one world for all sessions. Introduce runtime ownership and route each session to its current world. |
| `PlayerSession.Entity` | This is a handle in one EntityStore, not a transferable identity. Retain EntityId/account/connection and replace the handle on transfer. |
| `ShipSystem.RemoveShip` | Releases seats, puts passengers ashore, and calls SupportGone, which breaks/spills supported objects. Transfers need their own extraction path with none of those destruction semantics. |
| `ShipFrame`, `ShipSpace`, support links | Already preserve movement and objects relative to a moving ship. Keep these local poses and stable entity IDs across world changes. |
| `GameClient.OnWelcome` | Disposes all modules and prediction state and builds a new world. Do not use another Welcome as a seamless transfer. |
| `GroundView.OpenWorld` | Opens once, returning when its camera exists. Needs an explicit prepared-world transition without recreating HUD/ship/camera. |
| `PlanetGlobals` | Shader centre/radius are global. Two detailed surfaces cannot activate them simultaneously. Prepare the next world inactive; draw distant bodies with separate materials/transforms. |
| `PersistenceWorker`, `GameDatabase.SaveZone` | Saves one world per transaction. Migrating a ship, passengers and contents requires one transaction spanning the complete transfer bundle. |
| `SaveZone` removal SQL | Ship/thing/building deletions use ID alone. A late source save can delete a migrated destination object; source save bookkeeping and ownership guards must change. |

## Dependency order and file ownership

### T1 — Metric layout and frame transforms

Add a deterministic, data-driven playable system layout beside the galaxy model. It maps existing system/body IDs to
compressed metric positions, scaled radii and transition volumes. Physical orbital data stays source data; map
coordinates remain presentation data. Distance and speed defaults should produce a short first route, with measured
journey time recorded before expanding the layout.

Define a stable region identity: system ID, region kind, body ID where applicable, and revision. Define transforms
between region-local coordinates and system coordinates, including orientation and velocity. Use existing double
vectors for system metadata and ordinary local float physics. The standard Godot build remains appropriate.

Primary files: new layout/frame types in `src/Hollow.Galaxy` and `src/Hollow.Simulation`; extend `ZoneSpec` or introduce
an encompassing region descriptor. Tests must round-trip position, direction, rotation and velocity across regions,
and reject overlapping transition volumes or invalid body IDs. T2, T3 and T4 consume this contract.

### T2 — Multiple authoritative runtimes and real space flight

Separate the connection/account host from each world's zone, modules, relevance grid and save bookkeeping. Keep all
runtimes on a shared simulation tick initially; this avoids tick remapping and lets one process coordinate transfer
at a tick boundary. Give sessions a current runtime and route inputs, intents, proximity broadcasts and replication
through it. Teams and accounts remain host-wide; planet-specific simulation and presentation state belong to runtimes.

Introduce a real space environment with no terrain, air drag or planetary curvature carry. `ShipMotor` currently
expects a surface, applies radial gravity and rotates motion along the planet even at high altitude. Those behaviours
must be supplied by a planetary flight environment and be absent from a space environment. A fake zero-radius surface
would leave invalid ground/up calculations throughout the simulation.

Ship interiors need their own configured deck gravity: `ShipSystem.AddShip` currently passes the zone's movement
gravity to `ShipFrame`, which would become zero in a vacuum runtime. Define vacuum climate and equipment breathing
behaviour, retaining walkable interior collision and existing survival rules. Resolve oxygen support for the chosen
second planet as part of the playable journey rather than masking it with permanent god mode.

Primary files: `GameServer`, `ServerModule`, `PlayerSession`, server modules, `Zone`, movement frames, `ShipContext`,
`ShipMotor`, `ShipFrame`, climate adapters. Acceptance: simultaneous players in two worlds and space are independently
simulated; broadcasts and interactions never reach an unrelated runtime at coincident local coordinates.

### T3 — Transfer bundle and durable ownership

Add explicit extract/attach-for-transfer operations. The bundle contains the ship, every seat occupant and walking or
sleeping passenger, supported deployables/boxes/bags, their inventories and running machine state, and supported
transient objects that must survive. Keep entity IDs and ship-relative poses. Transform only coordinates expressed in
the source region, including ship pose, velocity, view directions and any detached object's state. Audit projectiles,
fires, fuses, crafting timers, respawn links and open container sessions; each needs a defined transfer policy.

Build transfer handling into the existing system/module registries rather than a second inventory or support system.
Do not invoke ordinary destruction/removal callbacks. On the destination, register the ship frame before restoring
passengers/support links, then replace each session's entity handle. Exactly one runtime may step an entity at a time.

Persistence needs an idempotent transfer transaction covering ship, characters and supported things, with source and
destination ownership and a transfer ID/revision. Remove transferred IDs from source save bookkeeping. Guard writes
and deletions against stale ownership; a queued source save must never overwrite or delete a destination record.
Recovery must choose the committed owner after a crash during preparation/commit, with no duplicate cargo or lost
sleepers. Extend `GameDatabase`, `PersistenceWorker`, ship/character/thing codecs and their tests as one package.

Acceptance: inject failures before and after commit, restart at each point, and count stable IDs and inventory totals.
Run with a box containing items, a sleeping passenger, a furnace and an attached timed object, not an empty skiff only.

### T4 — Prepared client transition and protocol

Add reliable prepare/ready/commit/cancel messages carrying region identity, transition ID, a shared commit tick and
the frame transform. Preparation gives the client the destination's deterministic surface description and initial
relevant state early enough to build physics and render resources while controls remain active. Include region or
transition epochs on unreliable snapshots and inputs; old packets must not be applied in the new frame. Invalidate
delta baselines at commit and send complete authoritative initial state. Preserve/re-map network entity IDs and ship
frame references consistently. Regenerate `Messages.g.cs` and bump the protocol version/schema.

Keep current and prepared client world contexts separately. Swap the active predictor/world at the commit tick,
transform queued views and unacknowledged input context, and seed the ship/passenger predictions from authoritative
states. Retain the connection, local inventory, ship visuals, HUD and camera. All clients travelling aboard the ship
participate, including a passenger who is still walking. Others retain their active world.

Preparation must begin before a reachable boundary using measured worst-case preparation time and ship speed. Source
authority continues while the destination is prepared. The overlap/transition policy must explicitly handle slow or
failed preparation and disconnected passengers; an invisible freeze or background teleport is not seamless travel.

Primary files: protocol definitions/generator, snapshot sender/receiver and delta contexts, `GameClient`, client module
lifecycle and predictors, server transfer module. Acceptance: packet loss/reordering and late prepare acknowledgements
cannot duplicate transfers, mix coordinates, reset inventories or strand one passenger.

### T5 — Continuous rendering and streaming

Add a system-space presentation with distant planet bodies at the metric layout's positions. Keep the ship and camera
in a stable local rendering frame while source terrain fades to its matching distant representation. Prepare the next
planet's terrain, collision corridor, sky and vegetation before descent. At the shared transition, apply the frame
transform to rendered entities and effects; activate destination globals exactly once and retire old terrain after
it is no longer visible. Do not restart the initial loading screen or shader/model warm-up.

The inherited atmosphere currently uses physical-planet assumptions beyond the small planet's limb. Fix and verify
that sky/cloud transition independently; a server-side continuous position is not sufficient visual proof. Maintain
the reused ground UI. Navigation/destination indicators and travel speed displays belong to the flight/map views.

Primary files: `GroundView` world lifecycle, `SurfaceView`, `RenderFrame`, `PlanetGlobals`, terrain/water/sky shaders,
ship/camera presentation and a new distant-body view. Acceptance: captured ascent, limb, approach and landing footage
shows no loading overlay, camera jump, missing ground, doubled planet or leftover source-world effects.

### T6 — End-to-end proof before expanding scope

Combine T1–T5 in the first complete scenario above. Measure per-frame times and transfer displacement, validate client
prediction with two players aboard, leave a third player on the source planet, and restart with the carrier in space
and on the destination. Verify both directions, unrelated simultaneous transfers, death/disconnection during transfer,
and persistent container contents. Only then extend the same contracts to more planets and interstellar corridors.

T1 can be implemented independently first. T2 and the T3 bundle/ownership design can proceed in parallel after the
region contract settles. T4 depends on T2/T3 commit semantics. T5 can build the distant-body renderer alongside T2,
but its active-world switch depends on T4. T6 is the integration gate for claiming connected planetary travel.
