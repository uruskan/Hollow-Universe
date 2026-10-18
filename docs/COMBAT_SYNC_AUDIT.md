# Combat synchronization investigation — 16 October 2026

Investigated at `fa7109a9` on `abtest-b`, at the owner's request. This is an investigation,
not a gameplay repair or visual acceptance. The original project and user saves were not used.

Follow-up: the owner authorized implementation, now described in [ATTACK_SYSTEM.md](ATTACK_SYSTEM.md).
The findings below preserve the pre-repair evidence; current validation and limitations are in
[DEVELOPMENT.md](DEVELOPMENT.md).

**Result:** reproduced three client cannon firing events from two chambered rounds while
authority fired only two; also reproduced a persistent melee action-tick disagreement and
predicted shoulder rockets surviving rejection or missing/reordered terminal messages.
The earlier melee work improved contact timing but did not complete attack reconciliation.

## Findings

### C1 — Twin-cannon prediction can show a third shot (high priority)

There are two independently demonstrated paths in `src/Hollow.Client/Modules/StationModule.cs`:

1. `OnShot` removes a pending prediction immediately on confirmation. `Display` overlays only
   pending shots over the last station record. Before the corresponding `ShipStations` record
   arrives, the spent chamber becomes available again. A further click emits another `Shot`.
   Server shot events are sent inside the simulation step; station records are sent with
   snapshot replication, so confirmation before updated state is a valid delivery sequence.
2. `Copy` derives `NextBarrel` from the latest `ShotTicks`. All fresh timestamps are the same
   never-fired sentinel; its first comparison nevertheless treats barrel 0 as already fired,
   making the client start at barrel 1. Authority's `StationState.NextBarrel` starts at 0.
   After the client predicts shots on barrels 1 then 0, server confirmation/state for its first
   shot on barrel 0 removes the first prediction and leaves the second reserving the already
   empty barrel 0. Barrel 1 looks available again. Another click emits a third visual shot.

The diagnostic exercised production `PredictTrigger`, `OnShot`, `Display`, and
`StationConvert.Apply`, with a real `IronWakeRig` authoritative station. Both message-order
cases produced three `StationShotView` events; three rapid authoritative presses with two
loaded chambers and no reserves produced 1, 1, 0 shots. The third authority result was
`Loading`. The second case used the actual first server shot and its actual station record.
This is evidence of false client firing feedback, not a third damaging server projectile.

The first wrong-barrel prediction also leaves a wrong launch origin: own rocket confirmation
currently binds only its server ID, without adopting the server's origin/velocity.
Both crewed battery types use this prediction path; the initial barrel-order defect concerns
alternate-fire siege batteries. The three-tube defense battery intentionally ripples three.

### C2 — Confirmation identity and predicted ammo need a shared lifecycle (high priority)

Station confirmation matches ship/station/successful-shot number, without checking the input
attempt, barrel or round. Numbers count successful server shots, rather than distinct input
attempts. Refused predictions, number reuse against stale records, and delayed reliable
confirmations after the 15-tick rollback timeout can therefore misassociate or replay effects.
These are source findings; the diagnostic directly proves the barrel mismatch and stale
reservation cases, not every timeout/rejection permutation.

`ProjectileLaunch` matches own predictions by shooter, sequence and battery *type*; it does
not carry the ship/station scope of a cannon attempt. Cross-station aliasing is an additional
unreproduced risk. Fixing only a sound or muzzle-flash cooldown would leave these state errors.

### M1 — Melee still lacks reconciliation when start ticks diverge (high priority)

`LocalMelee.Tick` and `CombatSystem.StepMelee` independently start swings on their first
observed held-primary tick. Input arriving after its authoritative deadline can be discarded;
`PlayerSession.InputFor` substitutes prior input for a missing tick. Local melee never consumes
an authoritative accepted start or reconciliation record. `FirstPersonView.OnHit` requires
the marker's `ActionTick` to equal the current local swing's tick exactly.

A scripted missing-first-input diagnostic using real `GameRig` and `LocalMelee` produced:

| Local swing | Authoritative swing | Damage tick | FPS contact tick equality |
| --- | --- | --- | --- |
| 31 | 32 | 40 | false |
| 55 | 56 | 64 | false |

Both hits damaged the target (remaining health 59). The one-tick disagreement survived the
next held swing. The production FPS guard rejects these otherwise legitimate contact
responses. This models the input boundary; it is not a captured packet-loss gameplay run.
The central HUD hitmarker and hit sound still consume server damage feedback independently.

The existing improvements are real: shared rounded strike ticks, retimed animation contact,
weapon/contact-point metadata, cancellation, and filtering stale contact feedback. They do
not guarantee identical start ticks or remove network delay from authoritative confirmation.

### M2 — Short melee clicks can disappear (medium priority)

`GameClient` latches `PrimaryPressed`, but its `Melee.Tick` call and authoritative
`StepMelee` inspect only held `Primary`. A click released before the next input tick can
therefore disappear. The diagnostic supplied `PrimaryPressed` without `Primary` and observed
zero local starts and no authoritative swing. Guns and bow release explicitly consume the
latched edge. Station fire also reads a held boolean and should receive equivalent edge tests.

The earlier intermittent `MeleeHitMarkerCarriesTheAuthoritativeToolTickAndContactPoint` failure
recorded no predicted swing and server action `None`, with valid target traces. Its 100 ms
wall-clock pulse and possible clock-correction pause are a plausible test-startup issue;
that historical log alone cannot prove the cause. It passed in this investigation's run.

Melee realism is separately limited: authority uses one thick aimed reach trace at contact,
not collision with the animated weapon mesh. The Quaternius hatchet trial intentionally
disables spatial contact adaptation. Shared timing is not proof of physical blade contact.

### P1 — Rejected gun/launcher shots leave predicted projectiles alive (high priority)

`LocalGun.OnUpdate` correctly restores authoritative gun/ammo state and emits `Refused`.
`SurvivalModule` subscribes to `ShotFired`, but does not subscribe to that refusal to remove
the corresponding projectile; no Godot `Gun.Refused` subscriber was found either.

The diagnostic ran real `LocalGun.Tick` and registered subscribers, then dispatched a
serialized refusal through the real message registry. Ammo returned to 1 and pending actions
to 0, but one own rocket with ID 0 and no endpoint remained and continued moving. `RaidView`
draws such projectiles with their model, trail and motor loop. The list's fallback expiry
is 15 seconds. This is a cosmetic projectile, with no authority to damage anything.

### P2 — Projectile impact delivery can leave rockets flying (high priority)

Server `SurvivalModule` sends gun/shoulder-launcher launches unreliably; battery launches are
reliable. Ends with nonzero sequence numbers are unreliable, including battery rockets.
Client `OnProjectileEnd` only binds an already-known server projectile ID, with no pending
terminal cache. Deterministic serialized-message diagnostics reproduced:

- A lost launch: delivered endpoint cannot bind the own rocket still carrying ID 0.
- A lost end: accepted/bound rocket has no endpoint and continues flying.
- End before launch: both messages arrive, but the early endpoint is discarded.
- Ordered baseline: launch then end produces one projectile with the authoritative endpoint.

Detonation feedback is a separate message path and does not retire the matching
`SurvivalModule` projectile; round detonations are also sent unreliably. Even a delivered
explosion does not repair the missing projectile endpoint, so a rendered rocket can outlive it.
These are controlled message-loss/order probes, not a measured frequency in live play.

### Other synchronization limits found in source

- Own projectile launch confirmation changes only `Id`, leaving a mispredicted trajectory.
- Remote/arrow flight starts at message receipt; `ProjectileLaunch` has no launch tick.
  Bow release animation is driven by the accepted launch, so it waits for network delivery.
- Hit-marker `ActionTick` is read from the shooter's current `LastActionTick` at impact,
  rather than carried from the originating projectile. Delayed shots can be stamped with a
  later action. Gun HUD markers currently do not use that field to correlate individual shots.
- Late gun intents rewind the origin/pose, but spread's velocity/crouch/airborne inputs use
  current character state. A shot crossing a movement-state change can disagree with local
  prediction. This was traced in source, not reproduced in this audit.
- The inspected damage paths remain server-authoritative. Projectile flight time plus
  confirmation latency normally separates firing from a hitmarker; that delay alone is not
  faulty hit registration. No extra authoritative ammunition/damage was demonstrated.
- No separate deployed Auto Turret combat implementation was found; it remains a design
  requirement. This investigation covers the implemented handheld and crewed battery paths.

## Repair direction (proposal, not implemented)

Use one scoped attack-attempt identity and lifecycle across prediction, acceptance/refusal,
ammo reservation, animation, projectile launch and terminal feedback. Keep confirmed ammo
reservations until a matching authoritative state incorporates them. Reconcile barrel and
launch state, carry the originating action identity into damage events, and make rejection
and terminal cleanup idempotent. Give important rocket terminal states reliable recovery
without requiring every cosmetic bullet update to be reliable. Reconcile melee starts and
consume latched click edges. Retain the engine-free, data-driven rule handlers and ground UI.

Regression coverage must include rapid clicks, fresh barrel order, confirmation before and
after state, rejected attempts followed by accepted attempts, delayed/lost/reordered messages,
reload and station changes, melee start shifts, and fast taps. Follow with actual two-client
Godot recordings under latency/jitter/loss, counting visual shots, server launches, ammo and
damage separately. The current battery loopback test spaces clicks at least one second apart
and checks counts, not barrel identity.

## Validation and reproduction

- `dotnet build HollowUniverse.sln --no-restore`: zero warnings/errors.
- `dotnet run --no-build --project tests/Hollow.Tests -- HeldActionTiming Gun Battery Raid BowPresentation`:
  **41/41 passed in 70.3 seconds**, including the existing melee hit-marker and lagged battery tests.
- Three standalone diagnostics reproduced the defects above; their successful exits mean
  the asserted faulty current behavior was observed, not that the defects are repaired.
- No production code/data changed, no full-suite rerun, no Godot launch or new rendered capture.
  The owner's running Godot process and unrelated `docs/REPAIR_ENVIRONMENT.md` edit were preserved.

Local source projects and logs are retained under `.artifacts/combat-sync-audit` (ignored
diagnostic artifacts, not part of the committed runtime). From the repository root:

```powershell
dotnet run --project .artifacts/combat-sync-audit/cannon-probe/CannonProbe.csproj
dotnet run --project .artifacts/combat-sync-audit/melee-probe/MeleeProbe.csproj
dotnet run --project .artifacts/combat-sync-audit/projectile-probe/ProjectileProbe.csproj
```

Logs: `build.log`, `focused-tests.log`, `cannon-probe.log`, `melee-probe.log`,
`projectile-probe.log`. Probes use reflection solely to inject deterministic input/message
boundaries into production client logic; cannon/melee authority uses existing real simulation
fixtures. They do not exercise GPU presentation, uncontrolled packet transport or real saves.
