# Handoff: continuing AB Test B after Codex stopped (19 October 2026)

Codex's weekly usage limit ran out at 01:01 local time on 19 October, in the middle of four threads.
It resets on 24 October at 23:40 local time. This file records the state Codex left and the plan to
continue. It was written by Claude from Codex's own session transcripts, the working tree, and a fresh
build and test run. The supporting data and tools are in `.artifacts/codex-handoff-2026-10-19/`.

**Phase 0 is done (19 October, about 02:00).** The exact tree Codex left is kept at
`refs/backup/codex-stop-2026-10-19`. The admin self-camera is committed as `74e51412`. The eleven
last-minute flora-integration edits are reversed; the eight files as Codex left them, and its plan, are in
`.artifacts/codex-handoff-2026-10-19/flora-integration-start/`. Everything else is committed as
`ba25d888`. The solution builds with 0 warnings and 0 errors, and the full suite passes 1,007 of 1,008.
A hosted Earth session on High reached play and saved a screenshot. The one failure,
`MultiplayerTests.TwoClientsSeeEachOther`, is intermittent and timing-related; see §5, Phase 1.

## 1. State when Codex stopped

| Item | State on 19 October, 01:30 |
|---|---|
| Branch and HEAD | `abtest-b` at `a13826d1`, 56 commits since the fork from `main` (`0cbc7ccd`) |
| Uncommitted work | 351 tracked files changed (+22,862 / −6,812 lines) and 579 untracked files (about 80,000 lines of code) |
| Staged in the index | The F1 admin self-camera, 25 files. That snapshot builds cleanly on its own. |
| Engine-free projects | Build with 0 warnings and 0 errors |
| Game client (`game/`) | **Does not compile.** `GroundView.cs` uses `PresentationSessionHost`, `SurfaceView.ConfigureAppearance` and `SurfaceView.RegisterAppearanceConsumer`, none of which exist. |
| Full engine-free suite | 1,006 / 1,008 in 638.5 s, no native crash. The two failures pass 3 / 3 in isolation: they are load-sensitive timing tests. |
| Jolt native crash | Not seen in the last two full runs. Codex hit it in two of its recent full runs. Cause unknown. |
| Saves | Protocol 39 and Earth save revision 5: the game refuses older saves and starts a fresh world. |

## 2. What was running when the limit hit

| Codex thread | Where it stopped |
|---|---|
| Review playable terrain progress (terrain coordinator) | Mid-turn on your question about why grass looks worse than the loading-screen image. It had found that `game/assets/loading/golden-meadow.jpg` is an older in-engine render, and that the main grass layer now disables the flowers and small plants seen in it. Its helpers were mid-way through three changes: world-phase lighting for grass (`SurfaceWorldPhases.cs` and four shaders), a natural-collision plan for the new physics ground path (a native run found a tile-identity mismatch, unfixed), and a repair tool for cliff normals. All three compile; none is validated. |
| Environment worker | Had just started the "flora production integration" (`.artifacts/environment-ownership/flora-production-integration-plan-worker-v2.json`). Three helpers edited eight files in the last two minutes. The host class they depend on was never written, which is what breaks the client build. The design loads an approved Earth flora package from `.artifacts/`, which git ignores, so it is not shippable as written. |
| Item art | The `salvage` model, textures, icons and catalogue entry exist. Its provenance file, review captures, build check and commit are missing. |
| Watcher automation "Watch terrain architecture quality" | Still `ACTIVE`, every 5 minutes (`~/.codex/automations/watch-terrain-architecture-quality/automation.toml`). It will wake when the quota resets and can message the terrain coordinator, which restarts work in this checkout. |

Other unfinished threads:

- **Cascadeur weapon clips.** The FPS comparison against the preserved Blender v3 stopped while correcting rifle-reload hand drift. It lives in `art/cascadeur_comparison/` and `.artifacts/character-trial-cascadeur-v3-comparison/` and touches no game files. It waits for your review.
- **Cascadeur posing.** That thread asked whether you want to pose an example yourself, and waits for your answer.
- **Rust reference study.** Its melee swing cues and held locomotion were saved unbuilt. They now compile and the suite passes, but they have no in-game check.
- **Defense battery interior sound.** `StationsView` still requests `<battery>.fire.interior`, which the catalogue lacks. A candidate sits in `.artifacts/p5-defense-interior-prep-r1/`, never installed.
- **Music.** Advice only: try ACE-Step 1.5 locally, Rust-like ground music, Elite Dangerous-like space music. Nothing is implemented.
- **The 16–17 October repair campaign** stopped on your order of 17 October. Its open items are in §5, Phase 3.

## 3. Work that was finished but uncommitted

Codex's rule was that the coordinator makes the integration commit. Your stop order on 17 October halted
the coordinator, and later threads committed only their own files when you asked. So most features
finished since 16 October existed only in the working tree until Phase 0 committed them in `74e51412`
and `ba25d888`.

| Feature | Date | Codex's own validation | Still missing |
|---|---|---|---|
| F1 admin self-camera | 16 Oct | AdminCameraTests, captures | — (committed as `74e51412`) |
| Attack lifecycle: attempt IDs, reconciliation, projectile endpoints ([ATTACK_SYSTEM.md](ATTACK_SYSTEM.md)) | 16–17 Oct | Focused attack tests | In-game combat feel check |
| Ship TPS input separation | 17 Oct | 33 focused, 27 native input checks | Full hosted tour |
| Ship attitude hold (N9) | 17 Oct | 46 state and network checks | Native loaded-gun and gunner sightline check |
| Frontier AK recoil, optics, support-hand grip, station aim | 17 Oct | 60 / 60 focused, both rigs | Your visual review |
| Admire key (N) and bow aim correction | 17 Oct | 26 tests, native samples, hosted tour | — |
| Compound utilities, resource nodes, fitted parts, ladders and climbing, gate locks, Earth layout 5 | 17 Oct | Build clean; focused 34 / 35, then the corrected fixture and door/lock checks 5 / 5 | In-world review |
| Roof packs without side panels (N10) | 17 Oct | 4 roof and 20 building checks | Visual and gameplay acceptance |
| Gib detail setting (N11) | 17 Oct | Native 44 / 44, UI 49 / 49, restart 36 / 36 | In-world destruction check |
| Admin clan-base preset and crafting HUD | 16–17 Oct | Clan base in a 64 / 64 foundation run; crafting HUD 16 checks in a 67 / 67 UI run | — |
| Map drag and diagnostics | 17 Oct | MapInteractionTests | — |
| Electricity, Wire Tool, device authorization; windmill, turret and SAM migrated ([ELECTRICITY_AND_AUTHORIZATION.md](ELECTRICITY_AND_AUTHORIZATION.md)) | 18 Oct | 43 focused, native captures | Multiplayer input capture |
| Contact feedback, material routing, audio and VFX loop budgets ([AUDIO_VFX.md](AUDIO_VFX.md)) | 18 Oct | 78 focused, native 721 / 41 / 33 | Listening review |
| Environment programme T0–T4 (see §5, Phase 2) | 18 Oct | Many focused and native fixture checks | Visual acceptance; most of it is inactive |
| Elite Dangerous-style travel design ([TRAVEL_IMPLEMENTATION.md](TRAVEL_IMPLEMENTATION.md)) | 18 Oct | Docs only | Implementation not started |

`.artifacts/codex-handoff-2026-10-19/uncommitted-files-by-thread.txt` maps every changed file to the
thread that last edited it.

## 4. Clear these before continuing

1. **Pause the Codex automation** in the Codex app, or set `status = "PAUSED"` in its `automation.toml`.
   Otherwise Codex and Claude may edit this checkout at the same time from 24 October.
2. **Do not resume old Codex threads** without telling them their state is stale. Several hold plans that
   no longer match the tree.
3. **Quit the Codex and ChatGPT apps.** About 8 GB of RAM is held by leftover helpers: 95 `node`,
   58 `node_repl`, 52 `python`, 25 `mcp-for-blender`, 203 `conhost` and 3 `codex` processes.
   Codex's memory gates (5.5 GiB to launch, 8 GiB for Maximum) kept stalling its own validation.
4. **Disk.** `~/.codex/worktrees` holds 18 GB. `admin-self-camera` (3.7 GB) is fully carried into this
   checkout; `terrain-rocks-port` (8.2 GB) is an older, superseded copy; `planet-foundation` (5.9 GB)
   belongs to the RustMVP repository; two folders are empty. `.artifacts/` is 6.1 GB.

## 5. Continuation plan

### Phase 0: secure the work and restore the build (done 19 October)

1. Record the whole working tree in a backup ref without touching HEAD, the index or any file:

   ```bash
   cp .git/index .git/handoff-backup.index
   export GIT_INDEX_FILE=.git/handoff-backup.index
   git add -A
   tree=$(git write-tree)
   commit=$(git commit-tree "$tree" -p HEAD -m "Backup: Codex working tree at stop, 19 Oct 2026")
   git update-ref refs/backup/codex-stop-2026-10-19 "$commit"
   unset GIT_INDEX_FILE; rm .git/handoff-backup.index
   ```

2. Commit the staged admin self-camera exactly as staged.
3. Restore the client build by reversing the eleven last-minute hunks of the flora integration. Copy
   the eight affected files aside first; they are the start of that integration. Tested on a scratch
   copy: the client then builds with 0 warnings and 0 errors.

   ```bash
   python -I .artifacts/codex-handoff-2026-10-19/revert_inflight.py .artifacts/codex-handoff-2026-10-19/last-edits . 093,092,090,089,088,087,086,083,079,078,077
   ```
4. Build, run the full suite, and host one game with a screenshot. Done as a hosted Earth session with
   its own scratch save on High at 1600 × 900: it reached play in 16.8 s with no engine errors, at 31 fps.
5. Commit the rest. Several features share files such as `Messages.cs`, `GameServer.cs`, `items.json`
   and `DEVELOPMENT.md`, so per-feature commits would not build alone. Done as one checkpoint commit,
   `ba25d888`, whose message lists every feature and its status.

### Phase 1: loose ends, each small and independent

- Finish `salvage` (provenance, captures, catalogue test, commit), then item-art batches 2 and 3 in
  [ITEM_ART_REPLACEMENT_PLAN.md](ITEM_ART_REPLACEMENT_PLAN.md): explosives, blueprint, research table, Wire Tool review.
- Install the defense interior sound from P5's candidate and test it.
- **Done (19 October):** `MultiplayerTests.TwoClientsSeeEachOther` and
  `AttackProjectileTests.GunAndRocketLifecycleSettlesThroughLatencyJitterAndLoss` (and, with them,
  `AttackStationTests.SpamCannotProduceMoreThanTwoShotsAcrossLatencyJitterAndLoss`, which failed once in a
  full run for the same reason). The cause was not only timing. `GameClient.SteerClock` waited a fixed 0.6 s
  after moving the tick clock, shorter than the server's 0.5 s margin window plus a round trip at 150 ms ping,
  so it acted on stale reports and swung the clock by tens of ticks for good once a join (one lost packet, a
  busy PC) had started it too far ahead; every swing pauses the client's ticks. That is fixed in the client
  (`Protocol.InputMarginWindowSeconds`), with `TickClockSettlesAfterASlowHandshakeAtALongPing` failing on the
  old cooldown. The three tests now run on `LoopbackRig(simulated: true)`: the rig steps the server and clients
  itself, a `SimulatedRelay` delays and drops datagrams on that clock, and `WaitUntilSettled` waits for the
  client's clock and ground before they count anything. See DEVELOPMENT.md, 19 October 2026.
- Guard `PhysicsWorld.BuildShape` so a malformed collision tile fails as a named managed error instead of
  a native access violation; capture a dump if the crash returns.
- Describe the new files in [CODE_LAYOUT.md](CODE_LAYOUT.md): 418 of the 420 C# files added since the fork are missing.
- Remove the stale worktrees and prune `.artifacts/` once Phase 0 is committed.

### Phase 2: decide the environment direction (your decision)

Facts: the programme added about 40,000 lines in two days (formations 8,000; environment descriptors and
leases 2,200; native lifetime checks 8,400; grass and terrain partials 3,100; tests 9,500; Blender
tools 8,600). World leases, descriptors and the pre-login world handshake run in the live game. The cliff
formations, the flora appearance package and the formation visuals are not active. Codex's own last
hosted capture still showed the straight cliff wall, unconvincing soil contact and uniform grass.

- **Option A, outcome first (recommended).** Stop adding infrastructure and fix what you see, judged by
  in-game screenshots: bring grass back to the loading-screen look, connect the concealment bushes
  ([TERRAIN_SCENERY_PLAN.md](TERRAIN_SCENERY_PLAN.md)), activate the formation cliffs only if they beat
  today's cliffs on screen, then terrain texture repetition. Keep the built infrastructure where it helps.
- **Option B, Codex's full T0–T6 plan.** Its gates call for 8 environments × 6 seeds, numerical budgets
  and repeated warmed routes before acceptance. Much slower to reach a visible change.

### Phase 3: remaining owner reports and the design roadmap

Open from [REPAIR_BACKLOG.md](REPAIR_BACKLOG.md): cliffs (N1, N4, N5), the unmineable rock and pickaxe
grip (N2), terrain repetition (N3), grass (N6), first-person tool framing (N7), the holo and readable
bullet flight (N8), and performance (N12). N9, N10 and N11 need only their in-game acceptance.
DESIGN.md features not started: connected space travel, supercruise, jumps and fuel scooping; oxygen
equipment; weather; modular ships and capture; Sleeping Bag respawn across planets.

## 6. Working rules from here

- Parallel sessions each get their own git worktree and merge back when green. No shared-checkout
  coordination with memory gates and hash manifests.
- Commit each finished change with a build and its focused tests. Run the full suite at milestones.
  Visual work is accepted from in-game screenshots, not fixture checks.
- Keep DEVELOPMENT.md entries to a few lines per change. No documentation-only "record" commits.
- Nothing the game needs at runtime may live in `.artifacts/`.
- Close Codex, ChatGPT and Blender before heavy builds or captures.

## 7. Evidence in `.artifacts/codex-handoff-2026-10-19/`

| File | Contents |
|---|---|
| `codex-threads.txt` | Every Codex thread since 15 October: title, dates, turns, helpers, last request |
| `codex-thread-final-messages.txt` | Each thread's last request and last reply |
| `codex-transcript-summary.jsonl` | Per-transcript summary of all 229 transcripts, including file edits |
| `uncommitted-files-by-thread.txt` | Each uncommitted file and the thread that last edited it |
| `last-edits/` | The exact hunks of the 94 file edits made in Codex's last twenty minutes |
| `revert_inflight.py` | Reverses selected hunks from `last-edits/`; used for step 0.3 |
| `flora-integration-start/` | The eight files as Codex left them before step 0.3, and its integration plan |
| `summarize.py`, `lastedits.py`, `owners.py` | The scripts that produced the files above from `~/.codex/sessions` |
