# Player corpses — legacy L15

Status: source, simulation/network/persistence checks and the actual hosted corpse flow are verified. All eleven
new rule/integration tests and the existing connected-looter death test pass, including a fall from 25 m above a roof
and a body supported by a removable storage box. The corrected lifetime and unrelated barrel fixtures passed in
the 25/25 focused run, followed by the shared full 376/376 run (`.artifacts/repair-full2.log`).

The build 15 hosted tour passed ordinary death/respawn, visible body, preserved loot, real melee harvesting, the
shared damaged-target health bar, same-entity backpack conversion and preserved backpack loot. Root inspected and
accepted the actual frames (`.artifacts/repair-gates15.log`). Six captures are retained in
`docs/images/abtest-b/repairs/corpses`; the failed scope frame from the later independent tour step is excluded.
The incomplete first corpse tour is superseded by this verified run, not counted as a pass.

The full-suite lifetime fixture explicitly checks the body-to-bag phase before final expiry. The fallback dead-player
ragdoll is also removed for late joiners, so it cannot reappear after the authoritative body has expired.

## Required behaviour

Every real player death leaves a visible body, including an empty inventory. God-mode protection remains at the
existing central death entry point and must create no corpse or duplicate inventory. The body's support is resolved
through the shared server collision query so a death on a foundation remains on it and one on a ship rides its frame.
All players can loot and harvest the body through the existing inventory, targeting and damage systems.

The server changes the body into a large lootbag after its configured body time or once harvested out. That transition
keeps the same entity and container, preserving every item count, condition, loaded magazine and attachment and any
open loot panel. Taking all inventory does not remove a still-harvestable body. Once converted, the bag follows the
normal loot lifetime and empty cleanup. A ship wreck is ordinary loot, not a harvestable human body.

Body duration, harvest health and resource yield are configuration, not client animation timers. Initial provisional
tuning is 150 seconds, 100 harvest health, 20 bone fragments and 10 fat, using existing resources and the shared animal
harvest-tool rates. These values are in survival.json's corpse section. No additional meat item is introduced in this
repair. The same cumulative yield calculation serves animal and player carcasses: faster tools shorten the work but
never increase the total resources.

## Integration boundary

`CorpseLifecycle` stores phase, the remaining server deadline, partial harvest and body/clothing appearance. The actual
loot container remains on `Corpse`, outside that state. `HarvestProgress` is the common proportional-yield calculation.
The rule tests cover exact deadline/one-time conversion, differing tool rates with exact total yield, invalid hits,
saved partial progress, and a bag that cannot renew its harvest.

PlayerSystem.Die resolves the supporting floor with SpillSystem's shared collision query and always creates a body.
ThingSystem's existing Thing receiver traces and harvests it; its lifetime step advances the phase before expiry.
Fixed building and solid deployable supports are registered in SupportSystem, and moving decks use the existing ship
support links. Corpses request a shared downward search to terrain height, bounded to 10 km, so a roof more than the
ordinary loose-item search's 4 m below is not skipped. Removing a support releases the body onto the surface below and
sends its new position. WorldCodec 7 preserves remaining world
time, partial harvest, appearance and support. Earlier corpse saves load as ordinary bags with their existing loot and
expiry; they never invent a fresh harvest allowance.

ThingSpawn includes one optional saved appearance record (owner ID, body type, seven clothing slots) and the server's
IsBody flag. ThingUpdate carries phase changes. PeopleView uses that saved appearance in a stable prone pose on the
server's support, including aboard a moving ship. It uses the existing large loot-sack model after conversion. Client
timers, distance and a reconnect no longer restart a body or turn it into a bag early. Dead player replicas are hidden;
the authoritative corpse is their only visible body, including for late observers after expiry. This bounded repair uses a
stable authored prone pose instead of a persistent physics ragdoll: Godot's local ragdoll cannot collide reliably with
the server-only Jolt building floors. The accepted build 15 [body](images/abtest-b/repairs/corpses/repair-corpse-body.jpg),
[harvesting/health bar](images/abtest-b/repairs/corpses/repair-corpse-damaged.jpg),
[backpack](images/abtest-b/repairs/corpses/repair-corpse-backpack.jpg) and
[preserved loot](images/abtest-b/repairs/corpses/repair-backpack-loot.jpg) show that bounded presentation.

CorpseIntegrationTests cover empty death and god protection; foundation support and removal; the real damage trace and
harvest pipeline; exact inventory preservation; save/restart of both phases, partial yield and v6 migration; a body on
a moving ship; and a network phase change followed by a late join. SurvivalTests also checks that a connected looter
keeps the same open container across body-to-bag conversion. The full suite and build 15 hosted flow above cover
the completed package; no persistent physical ragdoll is claimed.
