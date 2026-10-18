# Shared online attack lifecycle

Implemented 16 October 2026, following COMBAT_SYNC_AUDIT.md and the owner's authorization.
Validation results and remaining limits belong in DEVELOPMENT.md.

Every predicted melee attack, firearm/shoulder-launcher shot and crewed cannon shot has an
actor-scoped AttackId (server-assigned connection epoch, domain and monotonic attempt sequence). IDs identify attempts, including
refusals. Counts of successful shots remain resource state, never attempt identity.
The welcome message establishes that epoch; old projectiles surviving a reconnect cannot bind
to a new connection's shot even when its entity/network ID and attempt number are reused.

The client AttackLedger coordinates prediction, once-only presentation and authoritative
acceptance/refusal. Weapon handlers retain their data-driven timing and resource rules. The
server AttackJournal remembers bounded decisions for duplicate/reordered requests. Explicit
cancellation is terminal. Reliable unordered requests/results avoid head-of-line blocking while
bounded queues and server cadence/seat/equip/life checks protect authority. Character movement
and continuous station aim remain on their existing input stream. Server damage stays final.

The existing gun/reload/unload input sequence remains its transport adapter; firing maps into
the same attack ledger, journal and result event. Melee and stations use `AttackIntent`. All
domains share the session's fire/reload token bucket. Melee cancellation sends one frontier: cancel
all unfinished attempts through the highest abandoned sequence in this connection. Authority stores
one scalar, cancels queued/current swings, and refuses later arrivals below that frontier. Completed
attacks retain their accepted outcome. The frontier always advances; only optional acknowledgements
for unknown/duplicate cancellation IDs use the separate 32/second, 32-burst allowance. Queued attempts
receive their cancellation outcome immediately, and requests still in flight receive it on arrival.
Thus neither reordered packets nor a saturated response budget can revive an abandoned swing. Gun actions
(including reload/unload) and station requests use the shared `AttackOrder` gate to
retain attempt order when a packet overtakes a missing earlier request, bounded by the one-second
late-request window. Invalid or expired attempts are explicitly refused. This can delay authority
under loss; in-order requests have no added delay, and immediate local prediction still runs.
A known out-of-order gun refusal also waits its turn: advancing the cumulative ammo acknowledgement
past an unseen earlier shot would restore that shot's reserved round and allow an extra visual shot.

Stations check predicted ammunition, loader progress, cadence, mount limits, alignment and hull
clearance before submitting a request. An empty, loading, cooldown or otherwise locally invalid
press reports local refusal, sends no network attempt and consumes no sequence number. IDs cover
submitted attempts, including those later refused by authority; a formerly empty press cannot
become a delayed shot when an earlier prediction is refused. The server still validates every
submitted attempt against its current authoritative state.

Local animation, muzzle feedback and predicted projectiles start immediately; no round trip is
required to fire visually. An accepted shot retains its ammo reservation until the authoritative
resource state incorporates it. Confirmation cannot replay presentation. Melee uses stable
identity for contact feedback and reconciles authoritative start/strike/cooldown ticks.

Projectile launch/end carry attack identity and authoritative ticks. Owner launch/end and
important rocket lifecycle messages are reliable; ordinary remote bullet cosmetics remain
unreliable. Correction mutates the existing visual projectile rather than replaying effects.
Terminal-before-launch data and duplicate suppression are bounded. Hit feedback carries the
originating attack rather than reading whichever action the player is doing at impact time.

Server decision journals retain 256 outcomes per handler. The client keeps 4,096 completed
attempts and admits at most 128 unresolved attempts; handler resource queues are also bounded.
Projectile terminal/launch caches retain 4,096 IDs, while active long-flight projectiles remain
eligible for reconciliation even after many newer short flights finish. Server rockets remember
their launch viewers so a terminal reaches those viewers after leaving normal relevance range.
Ordinary bullet owners receive reliable launch/end; distant observers need no guaranteed
cosmetic trail. This changes the wire schema and introduced protocol revision 31; subsequent
protocol revisions retain this contract. Client and server must use matching builds.

Required validation: rapid/short clicks, two cannon chambers, refusal followed by acceptance,
confirmation/state ordering, cancellation and tool/station changes, latency/jitter/loss,
endpoint-before-launch, duplicate delivery, real rendered feedback, and measured server/client
cost. Physics realism beyond the existing melee trace is separate from synchronization.

Regression entry points:

```powershell
dotnet run -c Release --project tests/Hollow.Tests -- Attack
dotnet run -c Release --project tests/Hollow.Tests -- HeldActionTiming Gun Battery
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 30 --warmup 10 --script fight --kit gunfight-kit --lag 120 --jitter 30 --loss 5
```

The rendered ground tour's `attack-sync` step loads exactly two rounds through the ordinary
inventory/loader path, seats a gunner, spams the trigger, checks matching identities/barrels,
and captures loaded/first-shot/settled views. Use a fresh diagnostic database and `--tour-steps
attack-sync`. Network regressions use the actual client/server transport and conditioned UDP relay;
deterministic tests separately deliver confirmation/state and projectile launch/end in either order.

The separate `attack-launcher` step equips and normally reloads one shoulder-launched rocket,
then sends 21 rapid trigger presses with no reserve ammunition. It captures the loaded weapon,
predicted release and authoritative endpoint. Assertions require one visual identity, an accepted
decision for that identity, authoritative launch and endpoint applied to the original predicted
projectile, empty ammunition after the resource update, and no pending attempt or leftover projectile.
Use a fresh diagnostic database for each run. A bounded rendered example, after a completed build:

```powershell
& $env:GODOT --path game --resolution 1280x720 -- --view ground --name AttackCheck --host --in-process --port 28231 --spawn origin --lat -30.486486 --lon 43.059333 --quality low --hour 10 --mute --db ../.artifacts/attack-system/launcher-fresh.db --tour ../.artifacts/attack-system/launcher-shots --tour-steps attack-launcher --lag 180 --jitter 35 --loss 15
```

`--in-process` runs the production server on its own thread and still uses the real UDP client
and impairment relay. It avoids an additional dedicated-server build for this presentation check;
the separate process/load-test results remain separate evidence. The existing `chop` and `gun`
steps provide melee and rifle captures; `attack-sync` covers the two-chamber cannon. Screenshots
must be inspected, and a completed numerical test does not establish rendered quality.

## Recovery validation checkpoint — 16 October 2026

The current source is implemented but **final validation is incomplete**. During recovery,
[the exclusive runtime policy](repair-tasks/RECOVERY.md) requires at least 5.5 GiB available RAM,
one explicit coordinator slot per job, and stopping the owned job below 2 GiB available or at
85% memory commit. The commands above are entry points, not authorization to bypass that policy.

- The surviving pre-recovery [build log](../.artifacts/attack-system/build11.log) completed with
  zero warnings/errors. Its [attack run](../.artifacts/attack-system/attacks11.log) passed 32/32,
  including real impaired-UDP melee (180 ms RTT, 40 ms jitter, 10% loss), guns/rockets (120 ms,
  40 ms jitter, 12% loss), and cannons (180 ms, 35 ms jitter, 15% loss). The cannon run showed
  two unique visual/authoritative shots, matching barrels and zero pending reservations.
- The earlier [100-player stand-in-world firefight](../.artifacts/attack-system/load100.log)
  measured 902 ticks: mean 9.32 ms, p99 16.6 ms, maximum 30.3 ms, none over the 33.3 ms budget,
  and zero disconnects at 120 ms added RTT, 30 ms jitter and 5% loss. This is not a full-Earth
  rendering benchmark. Its active cutoff had outstanding gun actions and did not test queue drainage.
- Subsequent cancellation-frontier/reply-budget protection, local cannon preflight, the initial
  confirmed-tracer origin correction, strengthened projectile spam coverage and the new rendered
  launcher step still need a fresh completed build and tests. The previous passing logs do not
  validate these later edits. Interrupted/native-failed full suites are not passes.
- The coordinator's recovery build on 16 October was stopped after 34.69 seconds when available
  RAM reached 1.566 GiB. It has no completed solution summary. Emitted DLLs are not accepted as
  fresh build evidence. No post-recovery attack test or GPU capture ran at this checkpoint.
- Final launcher source SHA-256: `game/scripts/Ground/GroundTour.Attacks.cs`
  `00B6707BFA773932422E93B5470370F4C588A75ADB412BF6CD3135B80BDC99AD`;
  registry `game/scripts/Ground/GroundTour.cs`
  `4348E7B5465C8632FCEF3FB1FB9BD86DF926370BC7F8AC4E7A5529C5BB77D1CC`.
  Source review and scoped whitespace checks passed; these hashes do not imply compilation.

Remaining queue: completed single-worker incremental solution build; focused
`Attack HeldActionTiming Gun Battery` tests from that exact build; one rendered low-quality
scenario at a time, including impaired-network captures; coordinator's integrated regression
checks; final evidence review and coordinated commit. No repeat 100-bot run is required by default.

Recovery resumed after Blender completed and memory recovered. The coordinator's
17:18 incremental build passed with zero warnings/errors. The subsequent
[focused run](../.artifacts/attack-system/recovery-combat-20261016-1719.stdout.log)
completed **64/65** in 80.6 seconds, with no guard stop or crash (minimum 3,118 MiB available,
maximum 58% commit). Its exact DLL hash and process/resource history are in the
[receipt](../.artifacts/attack-system/recovery-combat-20261016-1719.receipt.json).
The later melee cancellation and cannon preflight regressions passed; impaired cannon input
again produced two visual and two authoritative shots with zero pending reservations.
The strengthened rifle spam test exposed a real ordering failure: eight loaded rounds produced
nine predictions and eight authoritative shots, with attempt 4 refused as `Stale`. Guns sorted
already-arrived requests but could execute a later sequence before the missing earlier request.
The shared ordering gate and three deterministic regressions now cover that gap, out-of-order
refusal and bounded expiry. **This repair still requires rebuilding and rerunning the failed
network case; visual checks remain pending.** The failed run is retained rather than relabeled.

Prediction is immediate but provisional. A request arriving outside the bounded history window,
or after a seat/equipment/life change, can legitimately be refused; its speculative projectile is
then removed and its reservation released. No system can guarantee acceptance under unlimited
packet loss. Correctness here means the result converges to authority without duplicate damage,
duplicate presentation of one accepted attempt, or permanently orphaned speculative effects.
