# P6 / N9 ship attitude and gunner-view review — 16–17 October 2026

**Scoped code validation complete: the shared build passed with zero warnings/errors;
the two coordinated test batches passed 34/34 and 12/12, including all 19 new tests.
Source remains frozen. Loaded-gun/native visual/camera acceptance is still pending.**
Root granted the exact attitude/state/seat/engine/codec/test scope after the review below.
All eight existing-file hashes matched `.artifacts/recovery/p6-preimplementation-20261016.json`
before edits; the granted existing files had no staged or working diff. Twelve source/test
files changed (table below); ShipConvert did not need editing. Only this report was updated
among docs. No data, art, cameras, Earth source, original-project files, other owners'
modules, Git index or save database was changed by this implementation chat. This chat
started no build/test/import/native run, extra agent, other-chat message, staging or
commit. Root subsequently ran the guarded serialized validation recorded below.

The coordinator's prior foundation result was 64/64
(`.artifacts/recovery/integrated-checks/20261016-195242-foundation.*`, recorded in RECOVERY).
It predates this source change and is **not** evidence that this implementation passes.

## Implemented behavior and scoped validation

- Shared motor state now has separate activation and target quaternion fields, no new
  ShipFlags bit. Inactive target bits do not affect equality/hash or normal pilot replay.
- Successful real Unseat captures the exact current orientation before any next tick.
  Missing/mismatched helm cleanup uses the same reconciliation. Active targets are not
  recaptured during braking; each curved-world substep transports target with hull.
- Shortest-arc full-orientation error supplies bounded desired rates through the existing
  acceleration clamp, fuel scaling, gravity, skid and hull-contact rules. Residual spin
  can overshoot and must return to the captured target; no pose/angular-velocity lock.
- New activation requires a vacant helm, no scripted Autopilot, assist ON, exact-zero
  retained throttle/strafe/vertical and powered airborne state. Awake takeover, nonzero
  translation, assist OFF, Autopilot, engine OFF/exhaustion, landing, destruction, orbit
  and sleep end the hold. OFF clears immediately, including OFF/ON between ticks.
- **Final grant refinement over the earlier strict-occupancy proposal:** a restored
  sleeping helmsman preserves an already-active saved target through Reseat and Tick.
  Sleeping occupancy cannot create a new target. Awake Seat clears immediately; reconnect
  wake is detected before the first piloted motor step, including early-return paths.
- ShipCodec is v6 with a fixed 17-byte trailer after the unchanged v5 fuel fields. The
  target is stored in the body frame, old versions default inactive, malformed target
  quaternions disable hold, malformed activation/truncated trailers throw. The fuel test
  now constructs genuine v5 and v4 payloads instead of retaining a later trailer.
- P5's confirmed ownership boundary allows OwnShipState and ShipConvert to stay unchanged.
  Protocol remains **33**, generated schema **895BF6382E0F2DBC**. No NetGen, schema or
  terrain/save-identity change was made. No gunner-eye offset change was made.

Static inspection checked the authorized diffs and transition call sites. `git diff
--check` passed for the existing changed files; all twelve changed source/test files had
zero NULs and zero trailing-whitespace lines. These are text checks only, not compilation
or test results. No assertion in an existing ship/network/fuel test was relaxed.

### Executed serialized validation — root's guarded slots

Receipts, arguments and stdout were read from `.artifacts/recovery/integrated-checks`.
Root ran one job at a time; every receipt has `StopReason: null` (no guard stop).
Elapsed values below are the guard's wall times, which include process overhead;
the test runner itself reports 2.2 s and 45.8 s for the two passing batches.

| Evidence prefix | Result | Guard elapsed | Minimum free RAM | Maximum commit |
| --- | --- | ---: | ---: | ---: |
| `20261016-235119-build` | Initial build failed: 0 warnings, 2 errors (CS0246/CS0103, missing ActionInput namespace in new test line 38); production assemblies compiled | 42.79 s | 3.732 GiB | 63% |
| `20261016-235806-build` | Corrected shared build passed: 0 warnings, 0 errors | 5.76 s | 5.826 GiB | 58% |
| `20261017-000010-attitude-state` | State/persistence/fuel/repair/architecture: **34/34 passed**, exit 0 | 2.84 s | 5.844 GiB | 60% |
| `20261017-000306-attitude-net` | Network/parts/restart: **12/12 passed**, exit 0 | 47.04 s | 4.969 GiB | 62% |

The initial failed [build log](../../.artifacts/recovery/integrated-checks/20261016-235119-build.stdout.log)
and [receipt](../../.artifacts/recovery/integrated-checks/20261016-235119-build.receipt.json)
are retained. Root granted a namespace-only correction: `using Hollow.Simulation.Players;`
in `ShipAttitudeNetTests.cs`. No behavior or assertion changed. The subsequent passing
[build log](../../.artifacts/recovery/integrated-checks/20261016-235806-build.stdout.log)
and [receipt](../../.artifacts/recovery/integrated-checks/20261016-235806-build.receipt.json)
identify the corrected build. Both builds used `dotnet build HollowUniverse.sln
--no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -nodeReuse:false
-v:minimal`.

The [state batch log](../../.artifacts/recovery/integrated-checks/20261017-000010-attitude-state.stdout.log)
and [receipt](../../.artifacts/recovery/integrated-checks/20261017-000010-attitude-state.receipt.json)
record execution of the already-built test DLL with filters `ShipAttitudeTests`,
`ShipAttitudePersistenceTests`, `ShipFuelTests`, `ShipRepairTests`, `ArchitectureTests`.
The [network batch log](../../.artifacts/recovery/integrated-checks/20261017-000306-attitude-net.stdout.log)
and [receipt](../../.artifacts/recovery/integrated-checks/20261017-000306-attitude-net.receipt.json)
record filters `ShipAttitudeNetTests`, `ShipNetTests`, `ShipPartsTests`,
`IronWakeNetTests.ASleeperAboardComesBackAboard`. The existing 900-tick flight retained
exact ship and seated-character prediction, and the network assertions retained zero
normal pilot corrections. These are scoped code, engine-free simulation and network
results, not a full-suite or rendered-game acceptance claim.

Coverage in those passing batches:

| Test filter(s) | Purpose / count where new |
| --- | --- |
| `ShipAttitudeTests ShipAttitudePersistenceTests` | 16 new tests: settled/max/coupled spin return, q/-q and half-turn direction, authored torque/rate limits and fractional fuel, open-air and two-substep curved flight, exact zero/terminal gates, real helm/gunner/takeover/engine/disconnect/cleanup transitions, actual sloped landing, equality, v1–6 fixtures, changed frame, malformed/truncated data and sleeping restore/wake |
| `ShipAttitudeNetTests` | 3 new tests: exact takeover motor prediction and eight-input rewind/replay, stale ownership packets, actual two-client helm release/hold/takeover with a delayed/jittered gunner and zero normal pilot corrections |
| `ShipFuelTests ShipRepairTests` | Existing fuel authority, final fractional burn, cruise/engine, small-craft exit, turn rate, controls and engine-off prediction regressions; only the old-format fixture construction changed |
| `ShipNetTests ShipPartsTests` | Existing strict exact prediction, observer flight, both seated clients, every release reason and old seat/door save compatibility; assertions unchanged |
| `IronWakeNetTests.ASleeperAboardComesBackAboard ArchitectureTests` | Existing real server restart/aboard restoration and architecture checks |

The new convergence/contact/network assertions passed in these batches. Runtime
acceptance still needs the coordinated playable sequence and
screenshots/recording: tilt with zero translation, leave helm with residual spin, operate
loaded guns, verify host/guest attitude, then take helm and steer normally. The new
network test covers occupancy/pose/prediction, not loaded cannon fire or visual sightlines.
P5's actual attack/ammo tests and the six-camera sightline review remain separate. No
native rendered-game or Maximum-world job was requested or launched by this report update.

### Frozen changed-file hashes (SHA-256)

| File | SHA-256 |
| --- | --- |
| `src/Hollow.Simulation/Ships/ShipState.cs` | `6CD6B49A0BA58819863002D40F1FDF4BAB93C189BCCEA1A2766FCF0EF93DB292` |
| `src/Hollow.Simulation/Ships/ShipMotor.cs` | `E589A3C04547E48F0AF793B779EB4E710EAA7DB272673B505E6E35CE2BE24B2F` |
| `src/Hollow.Simulation/Ships/ShipAttitudeHold.cs` | `5204E7A53E56A909D01810DDB233376FAE560AB86F6FF512573859B01DDAEA66` |
| `src/Hollow.Simulation/Systems/ShipSystem.cs` | `01A9405CF79BD45713FC10861741052E27D0103C933C287CFFBD4215E2045639` |
| `src/Hollow.Simulation/Systems/ShipSystem.Seats.cs` | `3CA150CC11D424E820253C54A038D4C706865A4CA5A0DEE69125CAC3455D70DE` |
| `src/Hollow.Simulation/Systems/ShipSystem.Engine.cs` | `BDD42FD0DC6228224437681E89DFB624F83778BE479C720815D9CA3B4D5B8CE0` |
| `src/Hollow.Simulation/Systems/ShipSystem.Attitude.cs` | `85D7515B3269F4F6556725AB0686FF9B7C70023DE5B5F6C459031CD7DDF784E5` |
| `src/Hollow.Persistence/ShipCodec.cs` | `5FE656BECEFDEDF4AF3E1E1A7D05155883FC983E6D94AA68CDE8C265EBB25057` |
| `tests/Hollow.Tests/ShipFuelTests.cs` | `2E057DAF41B90B9BC3D5BCD9124B74B2627AC9B28AAB46B68DBCE219B73F3FA9` |
| `tests/Hollow.Tests/ShipAttitudeTests.cs` | `3D6922EC71BD8417C47D2308DB1066724400AFEAB4618F4775A33EF8C4760676` |
| `tests/Hollow.Tests/ShipAttitudePersistenceTests.cs` | `B80E07AD1BC00D5AA56CB46A80F4830C3671996FDD404F348D21FD537E15048D` |
| `tests/Hollow.Tests/ShipAttitudeNetTests.cs` | `D651BB33EB3B21E2FF5E5A638462A917ECD42115DCBD5B0B66E0F82FE535FE4A` |

## Earlier source review and proposal

The following records the review that preceded the source grant. The implementation
checkpoint above supersedes its read-only status, proposed ownership and sleeper/wire
decision points; it does not turn the earlier observations into runtime evidence.

Reviewed DESIGN (especially TRAVEL-01/02, UI-04, SHIP-01–05, WORLD-05), RECOVERY,
REPAIR_BACKLOG, COMMON, P6-ships, REPAIR_SHIPS and applicable architecture/frame,
authority, shared-system, persistence and prediction rules. N9 supersedes the earlier
requirement to return the unattended ship toward level. Direct travel, ground UI,
finite projectile flight and independently operated cannon stations remain requirements.

## Confirmed code findings

### The unwanted levelling has an explicit cause

1. `ShipInput.Unattended()` in `src/Hollow.Simulation/Ships/ShipState.cs:84` retains
   throttle, strafe, vertical command and AssistOff; it clears pitch/yaw/roll and boost.
2. `ShipSystem.Seats.cs:236` (`Unseat`) uses that function when the helm empties.
   `Seat` at line 135 and missing-occupant cleanup at line 290 also sanitize controls.
3. `ShipSystem.cs:265` repeats unattended sanitization unless the pilot is awake or
   `ship.Autopilot` is set. Lines 266–273 retain engine operation unless explicitly
   stopped, then call the shared `ShipMotor.Step`. Thus leaving the helm does **not**
   simply turn off assist or the engine. That prior cruise repair must be preserved.
4. `ShipMotor.cs:194–200` calls `HoldLevel` for an assisted, powered, non-idling ship.
   `HoldLevel` at line 514 interprets `abs(input.Pitch/Roll) < 0.05` as a request for
   `-LevelReturn * currentAngle`, clamped to the corresponding angular speed limit.
   There is no vacant-helm/attitude-hold distinction in this motor call.
5. Iron Wake's `ships.json:81` enables 35°/25° pitch/roll limits and return coefficient
   1.4. Therefore the cleared controls directly activate return-to-level. This is a
   confirmed code cause of N9, not an inferred rendering problem.

The powered branch still compensates gravity and brakes linear/angular velocity within
thrust and torque limits. Fuel scales its available force/torque, and collision/skid
impulses remain active. Zero *commanded translation* is distinct from zero physical
thrust: a tilted hover still spends fuel counteracting gravity.

`ShipSystem.Stations.cs` operates after the motor step. The reviewed station aim/fire
path does not reset the ship's rotation; a gunner's input is routed to its station,
while only the actual pilot can assign `ship.Input` in P5's server `ShipModule.cs:229`.
There is no evidence that cannon firing itself causes the level return.

### Coordinate, prediction and save semantics

- `ShipState.Rotation` maps ship axes **+Z nose, +Y up, +X left** into the zone frame;
  angular velocity is in ship-local axes (positive X nose-down, Y nose-left, Z roll-right).
- Planetary up is `ZoneFrame.UpAt(position)`, not global Y. `ShipMotor.cs:383–393`
  parallel-transports rotation and velocity by `Facing.Turn(oldUp, newUp)` as the ship
  moves over the curved world. A local attitude hold must retain this behavior. A fixed
  world quaternion would gradually change pitch/roll relative to the local horizon.
- `ShipPredictor.cs:124–132` invokes the same motor for the pilot, using quantized
  `ShipConvert` input and the authoritative EngineStopped flag. Non-pilots receive the
  ship through remote snapshots/interpolation; the own-ship motor is not run for a gunner.
- `ShipCodec` is version **5**. It stores rotation in the body's frame, spin in ship-local
  axes, flags and authoritative fuel. Version 4+ retains translation and assist but no
  turning/boost command (`ShipCodec.cs:121–125,234–239`). No stored attitude target exists.
- `ShipConvert.ToOwn/FromOwn` and `ShipState.Equals` currently cover rotation and spin,
  but no hold target. Adding a target to motor state would require explicit save and
  reconciliation treatment; a hidden client-only target is not acceptable.

### The gunner eye path is authored and already raised

Hosted gunner first person uses `GameClient.RenderEye` (`GameClient.cs:295`), whose
seat override is `Modules/ShipModule.cs:217–231`. That calls
`ShipSeats.EyeInFrame` (`ShipSystem.Seats.cs:489`) with drawn/predicted mount parts and
transforms through the drawn ship pose. `ShipInterior.SocketAt:332` carries the socket
through its moving part. `ShipLayoutReader.Point:47–53` applies the authored offset
before that part transform. The offset is **part-local**, not planet/world Y.

All six gunner cameras attach to their station's **yaw** part, not its barrel pitch
or recoil part. `ships.json:107–127` already adds 0.60 m local Y for siege eyes and
0.25 m for defense eyes. Neutral positions derived from the existing JSON are:

| Station | Raw eye Y | Effective eye Y | Eye above seat socket | Authored occupied-head maximum Y |
| --- | ---: | ---: | ---: | ---: |
| Siege_Forward | 10.31 | 10.91 | 1.39 | 10.59 |
| Siege_Dorsal | 13.39 | 13.99 | 1.39 | 13.67 |
| Defense_F_P / Defense_F_S | 9.18 | 9.43 | 1.04 | 9.46 |
| Defense_A_P / Defense_A_S | 9.90 | 10.15 | 1.04 | 10.18 |

All lengths are metres in the authored ship frame at rest. Occupied-head bounds are
asset metadata, not a measurement of the rendered animated character. Siege's current
camera is already 0.32 m above that authored head envelope, so another blanket upward
offset risks a visibly detached viewpoint. This does not prove current sightlines clear.

`GroundView.cs:559–630` uses this eye for gunner first person; turret zoom changes FOV,
not eye height. Third person orbits `Client.RenderEye` and clamps against terrain, not
the ship interior. Camera axes use the drawn frame's up. The separate `ShipCamera`
cockpit/chase helper is the **pilot** path and is not the first gunner-eye fix location.

The reported low/blocked view is not yet reproduced at a known station/aim/attitude.
Static inspection confirms where its height originates, but does not establish a missing
offset, wrong transform, actual near-plane intersection or which barrel/shield blocks it.
Yaw-mounted eyes stay still as barrels elevate: aim-dependent obstruction is a concrete
case to inspect, not a confirmed defect at every station. The station's separate Camera
metadata is not the hosted RenderEye source; editing only that metadata would miss the
effective `ShipSeatDef.Eye` configuration. Do not hide the hull or alter barrel direction
or muzzle origin to make the camera appear clear.

## Captured-target proposal after the coordinator's contract decision

**Selected contract:** retain the attitude at the actual helm release, including yaw,
relative to the parallel-transported local planetary frame. Residual spin may carry the
ship past that attitude while bounded torque brakes it; powered assist must then return
to the captured target. The earlier two-file, zero-rate damping proposal is superseded:
it would settle at a different angle and does not meet this contract. At the authored
28°/s pitch rate and 75°/s² acceleration, ideal limit braking alone travels about 5.2°.
The contract is decided; production edits and runtime work remain ungranted.

### Minimal state and one shared lifecycle

Add `bool AttitudeHoldActive` and `Quaternion AttitudeHoldRotation` to `ShipState`.
The quaternion maps ship axes into the **current zone frame**, like `Rotation`. It is
the commanded orientation, not a second physical pose. No heading/Euler fields, timer,
stored planetary basis, ship-kind branch or network-controlled hold button is needed.
Keeping it in motor state makes each step, save and replay dependency explicit.

A small engine-free `Ships/ShipAttitudeHold.cs` should own capture, clear, target
validation and target-rate calculation. Capture normalizes the current rotation once;
clear sets inactive and resets the unused target to identity. `ShipState.Equals` must
compare activation and, only when active, target components. Inactive default structs
(whose unused quaternion is zero) must compare like inactive cleared states. Include
the same effective fields in hashing; never compare an inactive target as live state.

Put the authoritative occupancy/input predicate and its call into a small
`Systems/ShipSystem.Attitude.cs` partial. All existing seat/engine/tick paths use this
one helper. Eligibility is **actually unpiloted** (`ship.Pilot.IsNone`), not merely
"no awake pilot"; no scripted `Autopilot`; assist ON; effective engine command ON;
positive authoritative fuel; no EngineStopped, Destroyed, InOrbit or Landed state;
and retained `Throttle == 0 && Strafe == 0 && Vertical == 0` exactly. Negative vertical
and tiny nonzero commands end eligibility too. Do not use `WantsMotion`, a deadband,
physical velocity or physical thrust as this predicate. Gravity-compensating thrust
and residual translational motion do not disqualify a commanded hover. Momentary boost
input is already released by `Unattended`; preserve the existing boost timer/force
behavior rather than changing its duration as part of an attitude repair.

At release/engine events, derive the effective powered command from the authoritative
EngineStopped/EnginesOn state after `SyncFuel`; do **not** read raw
`ship.Input.Engines`, which is server-only and is not populated by control conversion.
For an actually vacant helm this is the same engine decision the current Tick makes.
At Tick use its already-derived effective input. Share this reconciliation entry point
so event capture cannot accidentally defer to Tick because the raw input flag was false.

| Transition | Concrete capture/clear timing |
| --- | --- |
| Successful real helm release | In `Unseat`, after clearing the matching helm occupant and sanitizing its input, synchronize fuel and reconcile hold **before returning**. Capture the current rotation if eligible and inactive. This is the release attitude, before the next motor integration; do not wait for braking or for a later Tick. A refused `LeaveSeat`/blocked `Release` never captures. |
| Missing/mismatched helm occupant | In both direct occupant-removal branches of `CheckSeats`, sanitize the helm input and call the same reconciliation helper. Non-helm removal does not touch the target. |
| Tick fallback | After `CheckSeats`, `SyncFuel` and effective input/engine selection, immediately before `ShipMotor.Step`, reconcile once. Preserve an eligible active target exactly. Capture only inactive-to-eligible transitions, including a legacy restore or an unoccupied engine restart. This also clears an externally changed Autopilot/translation/assist mode before the motor uses it. |
| Helm takeover / restored helm occupant | `Seat` clears hold in its existing `Flies` branch before any carry/snapshot/motor step. `Reseat` already calls `Seat`, so restored sleepers in the helm also clear it. A gunner/passenger taking or leaving a seat preserves a valid vacant-helm target. |
| Engine switch | Successful `SetEngine(false)` clears immediately, even if switched off and on between simulation ticks. Successful ON reconciles against the current pose and eligibility. Failed permission/no-fuel interactions do not alter hold. |
| Nonzero translation / assist OFF / scripted Autopilot | Clear on authoritative reconciliation before the next motor step; coming back to neutral eligible hover captures the then-current pose. Never reuse the previous target. No new packet input can directly activate it. |
| Exhaustion / actual landing / destruction | Clear in the motor on the step that establishes the terminal condition, before returning; include early destroyed/orbit/sleep exits and recheck after finite-state rollback so a copied start state cannot revive an expired target. Clear also in ShipSystem's already-destroyed branch before its skip, for destruction outside the motor. The final fractional fuel tick still gets only its existing fraction of torque/thrust. |

There is no new target when a disconnected sleeper remains in a blocked helm: the seat
is still occupied. A successful disconnect release captures exactly like other actual
unseats. This follows the selected **unpiloted** contract without changing exit rules.
`Touching` alone is not the new landing criterion: use the motor's actual `Landed`
result. Existing contact idling still controls whether torque is available; if its
existing engine logic removes the effective powered command, hold becomes ineligible
for that reason. Do not change skid support, contact idling or engine latching to keep
a controller artificially powered.

### Shortest-arc controller within the existing torque path

The motor signature need not expand: the authoritative system has set explicit state,
and ordinary pilot states are inactive. Add motor-side checks for power, fuel, assist,
neutral translation and terminal flags so an invalid active state cannot drive torque.
In the powered assisted branch, select target-hold rates **instead of** `HoldLevel`
when active; otherwise retain the current piloted/cruise path. Never apply both.

For normalized current `q` and target `t`, calculate local error
`e = normalize(conjugate(q) * t)`. This order matches the existing
`ShipMotor.Integrate` (`q * delta`). Choose the shortest quaternion hemisphere (`W >= 0`);
at exactly 180° choose a deterministic sign using the first nonzero X/Y/Z component.
Compute `angle = 2 * DMath.Atan2(length(e.xyz), e.W)` and the local rotation vector
`error = e.xyz * angle / length(e.xyz)`, with a small-angle finite-safe branch. Thus
quaternion sign equivalence cannot cause a full revolution and yaw is retained too.

For each local axis, propose this bounded desired rate, using that axis's existing
`TurnAxis.MaxRate` (v), `Acceleration` (a) and fixed step dt:

```text
if v <= 0 or a <= 0: desiredRate = 0
otherwise:
    distance = abs(errorAxis)
    gain = a / v
    speed = min(v, gain * distance, sqrt(2 * a * distance), distance / dt)
    desiredRate = sign(errorAxis) * speed
```

The proportional cap tapers the rate near the target; the stopping-distance cap avoids
demanding a rate whose ideal braking distance exceeds the remaining angle. This is a
proposed bounded controller, not a claim of monotonic motion in coupled axes or under
contacts. Use existing deterministic math conventions (`DMath.Atan2`, arithmetic and
square root); no new transcendental library or renderer dependency. Deriving the gain
from authored rate/acceleration avoids overloading `LevelReturn`: that field can validly
be zero to disable level return, while the new hold contract still applies. It also
avoids adding tuning fields or changing ship data before measured need.

Feed these desired rates through the existing `(rates - angularVelocity) / dt`, drag
compensation, per-axis acceleration clamp and fuel scaling. Do not assign Rotation,
AngularVelocity or Velocity to the target, zero residual spin on capture, bypass gravity,
remove collision impulses or guarantee recovery when available torque/fuel cannot do it.
An impact can displace the hull; while still eligible, subsequent bounded torque returns
it toward the original target. Landing ends the target instead of fighting the ground.

### Curvature transport is part of the target, not a recapture

In **each existing motor substep**, immediately beside `Rotation = normalize(carry *
Rotation)` at the curved-world correction, also apply `AttitudeHoldRotation =
normalize(carry * AttitudeHoldRotation)` when active. Use the very same
`carry = Facing.Turn(under, over)` computed by that substep, including its movement and
contact correction. Do not reconstruct an end-of-tick shortest turn, use world Y,
recalculate yaw/pitch/roll, or recapture from the drifting physical pose. Multiple
transports are order-dependent; matching each carry preserves the intended local
planetary orientation through the entire path. Any finite-state rollback restores the
pair consistently, followed by terminal-condition clearing.

### Save format and migration

Propose **ShipCodec v6**, preserving the complete v5 prefix, then appending a fixed
17-byte trailer: active byte plus four target-quaternion floats. Store identity when
inactive. Convert an active target with the same `BodyTurn(zone) * targetZone` as the
physical rotation; decode with the same body-to-zone rotation. Spin remains ship-local.
A save made during braking must retain the *release target*, not replace it with the
current, temporarily displaced pose. No database schema, terrain identity, Earth save
revision or generation golden value changes are needed for this ship payload change.

Versions 1–5 decode with hold inactive, preserving their physical rotation, spin,
retained commands and fuel migration. The first eligible authoritative reconciliation
captures the restored pose because those formats contain no historical release target;
do not invent one. Version 6 retains a valid active target when restoring an unoccupied
ship, but the normal eligibility/`Seat` paths clear impossible occupied, off, nonneutral
or landed states before simulation. Do not recapture a valid saved target at `AddShip`.

Read the complete fixed trailer with ordinary truncation errors preserved. Accept an
active target only if every component is finite and the quaternion has a usable unit
length within the agreed serialization tolerance; normalize valid rounding differences.
Invalid target data disables hold rather than injecting NaNs or rotating toward identity;
the next eligible reconciliation captures the current valid physical pose. Malformed
active-byte values should be rejected as invalid data. Old binaries reject version 6;
save compatibility needs the same forward-version caution as prior codec bumps.

There is a specific existing fixture to update under its owner's grant:
`ShipFuelTests.ShipFuelV5AndLegacyV4RestoreExactlyOneAuthority` currently synthesizes v4
by removing the last eight bytes of the **current** encoding. After appending a trailer,
that no longer creates genuine v4 bytes even if a decoder ignores the trailing payload.
Use explicitly bounded v5/v4 fixture prefixes or an independent legacy fixture, keeping
all one-authority fuel assertions intact. Do not silently weaken or omit this test.

### Own-ship prediction and the wire boundary for P5 review

The preferred minimal design requires **no wire expansion if P5 confirms and tests the
following invariant**: an active target is possible only while no pilot owns the ship;
`Seat` clears it before the first own-ship snapshot and motor step, and a lost own-ship
snapshot stops that client's predictor. Server `ShipModule.OwnShip` currently emits
`OwnShipState` only to the actual helm occupant. Remote observers/gunners interpolate
the resulting physical pose; they do not simulate an unattended motor and need no target.
Autopilot is excluded from hold.

Under that invariant, `ShipConvert.FromOwn` produces inactive state (optionally setting
its unused quaternion to identity for clarity), and `ToOwn` never receives an active
target from the live ownership path. `ShipState.Equals` must ignore inactive target bits
as above. Existing pilot input replay remains the same motor path with hold inactive;
do not add a client-local inferred target or have the converter silently strip an active
target to conceal a broken ownership rule. Verify actual server header production and
client stop/reset transitions, not just a standalone converter round trip.

P5 owns the final protocol decision and any converter/server-module hunk. If inspection
or a real release/takeover/restart test shows active hold can enter an own-ship snapshot
or predictor, either fix that ownership violation within the selected unpiloted contract,
or explicitly expand OwnShipState with activation and target, regenerate the protocol,
bump its version and test replay. Do not assume the latter is necessary or edit the wire
without P5/root agreement. The shared-state and v6 persistence proposals are required
regardless; the conditional decision is about **wire** serialization only.

### Camera proposal

Reuse the existing per-seat `{ at, offset }` configuration; no new C# camera system is
justified by the static findings. Before selecting final numbers, inspect all six station
eyes through their existing shared transforms and mounted geometry. Adjust only the
specific blocked seat's local offset if it can provide clear aim without detaching from
the head or clipping at other angles. Keep yaw attachment and the seated body/grips unless
evidence shows the authored seat itself needs repair. A repair to authored seat/head
alignment must update its exporter and derived data together, not hand-edit generated
geometry. Any general collision-aware orbit change is separate scope from this eye fix.

## Exact proposed ownership, not an edit grant

| File / hunk | Proposed next owner and boundary |
| --- | --- |
| `src/Hollow.Simulation/Ships/ShipState.cs` motor fields / equality / hash | P6 proposed: active target in shared state; compare target only when active |
| new `src/Hollow.Simulation/Ships/ShipAttitudeHold.cs` | P6 proposed: pure capture/clear/validation and shortest-arc bounded desired rates |
| `src/Hollow.Simulation/Ships/ShipMotor.cs` assist rates, per-substep carry, terminal/early-return paths | P6 proposed: use target rates instead of level return, transport target, clear ended holds; retain existing thrust/fuel/contact and torque limits |
| new `src/Hollow.Simulation/Systems/ShipSystem.Attitude.cs` | P6 proposed: one authoritative eligibility/reconciliation helper |
| `src/Hollow.Simulation/Systems/ShipSystem.cs` destroyed skip / input selection / pre-motor call, 247–273 | P6 proposed: clear a destroyed hold and invoke shared reconciliation; preserve current controls and engine selection |
| `src/Hollow.Simulation/Systems/ShipSystem.Seats.cs` Seat / Unseat / both CheckSeats removal paths | P6 proposed: immediate takeover clear and exact release capture; no eye/socket or exit-rule edits |
| `src/Hollow.Simulation/Systems/ShipSystem.Engine.cs` successful SetEngine | P6 proposed, root must reserve: immediate OFF clear / fresh ON eligibility reconciliation |
| `src/Hollow.Persistence/ShipCodec.cs` version, v6 trailer, body-frame conversion / legacy default | Required coherent save change; **P5/root must assign the owner** before P6 or P5 edits |
| new `tests/Hollow.Tests/ShipAttitudeTests.cs` | P6 proposed: meaningful motor/zone/seat/controller/curvature cases using existing fixtures |
| new `tests/Hollow.Tests/ShipAttitudePersistenceTests.cs` | Proposed codec owner's tests: v6 release target, changed frame, genuine v1–5 fixtures, invalid/truncated trailer |
| `tests/Hollow.Tests/ShipFuelTests.cs` legacy v4 fixture construction only | Existing owner's coordinated hunk; retain all fuel assertions and make old-format bytes genuine |
| new `tests/Hollow.Tests/ShipAttitudeNetTests.cs` | Proposed P5-coordinated tests: actual own/remote transitions, takeover replay, delayed/reordered snapshots and restart ownership |
| `src/Hollow.Net/Protocol/ShipConvert.cs`, messages/generated protocol/version | P5-owned; preferred no wire expansion under proved inactive-own-state invariant; any change separately agreed |
| `src/Hollow.Client/ShipPredictor.cs`, client `Modules/ShipModule.cs` stop/reset/reconcile / SeatEye | Read-only verification initially; no predictor or eye-transform change established |
| `game/data/ground/ships.json`, derived `ships/iron-wake.json`, exporter, new station-view tests | **No camera/data edit proposed before reproduced sightline evidence**; later scope only for a demonstrated defect |
| `game/scripts/Ground/GroundView.cs` camera 559–630, input 755–783, SeatInput 975+ | Admin/P5 shared boundary; staged admin work plus other working changes exist. No P6 edit proposed initially |
| `tests/Hollow.Tests/ShipRepairTests.cs` | Admin owns staged OrbitCamera rename at 158/171/173. Run existing regressions later, do not edit/stage this file |
| server `Modules/ShipModule.cs`, client `Modules/StationModule.cs`, `ShipStations.cs`, `ShipSystem.Stations.cs` | P5 firing/identity/ammo ownership; exclude from P6 production patch |
| `docs/REPAIR_SHIPS.md`, architecture/design/development/backlog | Root-owned docs integration; this review only writes the present note |

At inspection there were no working diffs in ShipMotor, ShipSystem, ShipState, ShipPredictor,
client ShipModule, ShipCodec, ShipConvert or the two ship JSON files. That is a checkpoint,
not permission to edit them. Recheck owners/diffs before any future grant.

## Meaningful regression and gameplay acceptance scenarios

1. Through an actual Iron Wake helm, release an assisted, fuelled, airborne ship tilted
   in pitch/roll with retained throttle/strafe/vertical exactly zero. Capture the reference
   at successful Unseat, not after a tick. Exercise settled and positive/negative maximum
   pitch, yaw, roll and combined-rate releases. Assert the stored target is unchanged
   while braking and that the hull returns to it: proposed calm-air acceptance is under
   0.2 degrees orientation error and 0.05 degrees/second spin after 30 seconds. Also check
   equivalent q/-q targets, near-180-degree errors and no quaternion/velocity snap.
   These are proposed tolerances to validate, not measured results. A solution that simply
   stops at the post-braking angle fails, even if it does not level the ship.
2. Repeat in different planetary radial frames and while residual linear velocity carries
   the hull over curvature. Compare forward/up against a parallel-transported reference,
   accounting for quaternion sign equivalence; do not compare raw Euler/world-Y values.
3. Real helm exit versus blocked/refused exit; successful disconnect versus a sleeper
   still occupying the helm; both missing/mismatched occupant cleanup branches; gunner
   occupancy, reseat and immediate manual input. Reject every nonzero translation axis,
   including tiny/negative vertical values, and exclude scripted Autopilot/AssistOff.
   Clear on engine OFF then ON in the same tick, and recapture the new current pose.
   Ensure first piloted snapshot/tick is inactive and no stale target survives takeover.
4. Engine off/on, last fractional fuel tick/exhaustion, collision and landing on a slope:
   no powered hold while off/empty, no forced quaternion/velocity reset, no fuel duplication
   or bypass of skid/contact motion. Preserve nonzero unattended cruise/strafe/climb and
   existing small-craft airborne-exit momentum, 90° turn and steering/throttle regressions.
   In a controlled contact-free case, separately verify commanded angular acceleration
   never exceeds each authored limit, desired rate never exceeds its limit, and the last
   partial fuel tick scales available torque exactly as before. Physical spin displaced
   by an impact is not itself clamped to the controller's desired-rate limit.
5. Save during residual-spin braking, with release target different from current pose;
   reload in another zone frame and continue toward the transported original target.
   Exercise inactive/active v6, genuine v1–5 records, invalid/NaN/zero target, truncated
   trailer, occupied/sleeper restore and fuel units/reserve without duplication. Own-ship
   handoff/reconciliation must prove inactive-state equivalence and exact normal pilot
   rewind/replay; remote pose interpolation must follow the authority during the hold.
6. Two-client actual gameplay: host tilts/leaves helm; host or guest mounts each cannon,
   aims and fires loaded rounds, then returns to helm. Compare authoritative hull pose,
   both rendered poses, recorded impacts and normal manual response. Reuse P5's actual
   attack path without adding a fire shortcut or relaxing its cadence/ammo assertions.
7. Six station eyes at neutral, arc endpoints, min/max elevation, recoil, both hull tilt
   signs, first person and orbit, default/zoom FOV. Test unobstructed reticle/near-plane
   clearance from head/receiver/hull in the selected valid sight sector; distinguish
   mechanically/hull-blocked firing arcs from the player's general ability to look around.
   Verify local offsets transform with mount and ship, not global Y.
8. Actual passenger V/RMB orbit/wheel coverage, gunner LMB fire versus RMB orbit/first-person
   zoom (including held-button camera switches), pilot steering/throttle/Q/E/Space exit,
   one-slot fuel inventory and explicit engine radial. Retain screenshots plus action-cycle
   recordings at identical settings; existing Low-quality input-routing captures are not
   proof of loaded fire or clear sightlines in every seat.

No runtime slot is requested here. The selected captured-target contract now has a
concrete source/save/test proposal; **only this report has been extended**. Source,
tests, data, build, runtime and Git operations remain frozen until root grants a coherent
file scope and resolves P5's save/wire ownership. Camera offsets remain unchanged until
a sightline is reproduced. The Maximum world job remains under root's 8 GiB gate.

## Inspected source checkpoint

| File | SHA-256 |
| --- | --- |
| `ShipMotor.cs` | `BF7A6040C086FE26FA7EE6B694142E961F0A2412EA248267855E85BB9E551924` |
| `Systems/ShipSystem.cs` | `1289B31D6A516AE7886DDD76FF532411A4E30EEFA2DE46C70FBAFFFE08FFF0C4` |
| `ShipState.cs` | `39E57B2317E7750C635BF3C25295C3528CA0B5D90354D622FF629AC6EF527AF4` |
| `game/data/ground/ships.json` | `80AC84B5D17C0A6FE435A9BED958F9A7E3A9C8A0E8F0216E3783E760C16B555A` |
| `game/data/ground/ships/iron-wake.json` | `AD0DDB905A156D1B382DE243912391739B516DB5EBC53D2F5B53BC97ACB9AE66` |

These hashes identify the reviewed text/data; they are not a build or test result.
