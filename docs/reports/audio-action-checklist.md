# Audio action checklist — 16 October 2026

Snapshot of **235 events / 775 unique files**. The overview below groups the actions; the JSON has every event and variant, source segment (when exported), hash, stored measurement, actual inspected runtime path and candidate item-role consumer.

**Technical inspection is not listening.** This generator establishes catalogue coverage, recorded routing evidence, current file/manifest agreement and links to existing gameplay captures. It does not play audio, measure audible latency, assess timbre/masking or grant visual, audible or user acceptance. The owner's reported rejection and later narrow source-sample and grass-gameplay boink-absence results are recorded explicitly below; no agent listening or visual-timing review is claimed.

Reproduce: `python -I tools/audio/audit_catalogue.py --footstep-recipes`. Exact source segments are exported for 148 files (footsteps scope). Other events carry recipe-section provenance pointers, not a completed per-file source reconstruction. Full recipe enumeration is deliberately deferred because unrelated synthesis is expensive. Recipe evaluation can populate the ignored synthesis cache and run onset analysis; it does not encode game assets. Re-run after source or routing changes. JSON input hashes identify the exact inspected snapshot.

**Owner listening result: initial footstep repair rejected.** The owner listened to the first A/B reel and reports the boink remains throughout, mainly while walking and jumping. Removing generic heavy-impact thumps was insufficient. Base walking recordings were unchanged; their material identification, recorded contacts and processing require further diagnosis. The first repair must not be represented as accepted or as resolving the complaint.

**Later sample result: boink absent in both source sets.** For the six-second `source-candidates-v2/new-ground-contacts.wav` comparison the owner said, "Neither set has it". This result applies only to the four exact processed BSB1117 dry contacts and four exact processed BSB2889 organic contacts in that file. It does not accept the full source recordings, newly selected variants, modified gait processing, gameplay timing or the game's wider mix. The JSON preserves the audition file hash and exact selection/filter settings. The separate grass gameplay result follows.

**Grass gameplay: owner confirms the boink is absent.** The owner said, "okay gameplay-grass-mix.wav doesnt have it". This accepts absence of the reported boink in that exact retained grass gameplay mix, including its captured walking, running, crouching and landings. It does not accept the second verification recording, dirt gameplay, other materials, transitions, broader mix quality or the full catalogue.

Two actual hosted-client movement captures separately passed technical checks. The retained first run contains walk (11.25 m), run (21.81 m), crouch (5.2 m), three jumps/landings and silent idle. It logged nine walk, nine run and seven crouch contacts, one final walk contact during stop-run, and one landing per jump. Its Master WAV is 26.848 s and Footsteps WAV 26.933 s, with no clipped PCM reported by the coordinated recording check. The hardened repeat produced two valid, nonzero 26.848 s WAVs, each 0.010 s from the harness duration. These checks establish captured output and exercised grass actions, not realism or perceived timing. No owner audition of the repeat is claimed. Dirt gameplay recording remains open.

Retained evidence: [grass game mix](../audio/repairs/2026-10-16/gameplay-grass-mix.wav), [isolated grass Footsteps bus](../audio/repairs/2026-10-16/gameplay-grass-feet.wav), [grass trace](../audio/repairs/2026-10-16/gameplay-grass.json), [owner-auditioned source contacts](../audio/repairs/2026-10-16/approved-source-contacts.wav). The JSON separately summarizes the hardened repeat under `.artifacts/audio-first/gameplay-final`. Trace timestamps are sound-start requests, not sample-accurate audible onsets. The final engine catalogue check reported 235 events / 775 files and zero problems; engine loading/measurement is technical evidence only.

**Current integration:** grass uses the four selected BSB2889 organic contacts and dirt uses the four selected BSB1117 dry contacts. Walk/run share those contacts with gait cadence/level changes; crouch adds a 12 ms fade; landing plays one of the first three single contacts. These eight events use pitch 0.98–1.02 and volume jitter ±0.8 dB. Dirt additionally uses a gentle compressor (threshold −15 dB, ratio 1.5, attack 0.2 ms, release 25 ms) added after the source audition. That brought the sharp third contact within the unchanged loudness/peak bounds; dirt has not received user gameplay acceptance. The final file/provenance snapshot below reflects the integrated assets; only the retained grass mix has scoped runtime boink-absence acceptance.

The duplicate bow-release path is removed, dry fire now has a per-press consumer, and melee contact uses the confirmed attack's weapon. These are implementation findings, not audible acceptance. Shell playback and successful-repair audio still have no inspected gameplay consumer.

Natural gravel maps to dirt; there is no separate gravel event or jump-takeoff event. Exact prop/monument materials, live weather coupling, field-recorded animal voices, and complete gun/impact mixing remain open. A catalogue event may exist without an active gameplay trigger, and a gameplay action may have no catalogue event.

Playback uses shuffled variants and bounded pitch/jitter. Local feet are flat with -3 dB; remote feet are positional. 3D events use inverse-distance attenuation and occlusion, with distance layers/propagation only where configured. The report retains per-event numeric settings; nominal loudness and bytes do not establish realism.

## Required gameplay review

| Priority | Scenario | Evidence |
|---|---|---|
| P0 normal ground | Grass/dirt walk, run, crouch, jump takeoff/land, repeated jumps, small ledge and sprint-stop. Record event trace and real output together. | Owner confirms boink absent in retained grass mix. Broader quality, dirt, other materials and unexercised states remain open. |
| P0 state gates | Airborne, sleeping/waking, seated/unseated, dead/respawn, noclip on/off; moving ship stationary feet and walking deck. | Owner confirms boink absent in retained grass mix. Broader quality, dirt, other materials and unexercised states remain open. |
| P0 materials | Grass, soil, gravel, sand, stone, snow; twig/wood/stone/metal/armoured floors; floor edges, scanned cliffs, monument interior; lake/stream shallows and swim transitions. | Owner confirms boink absent in retained grass mix. Broader quality, dirt, other materials and unexercised states remain open. |
| P1 confirmations | Own and remote body/head/kill, armored hit, miss, melee hit; quiet and competing gunfire/explosion. Correlate accepted hit tick, animation contact, playback start and perceived click. | STILL OPEN — no recording/listener acceptance attached |
| P1 weapons | Bow draw cancel/release/end, rifle/pistol/launcher shot, dry trigger, reload cancel, shells/ricochet; near/mid/far listener and blocked/unblocked line. | STILL OPEN — no recording/listener acceptance attached |
| P1 gathering | Tree/ore first hit, repeat hit, weak spot, stage change and depletion; correlate frame spike and waveform onset. | STILL OPEN — no recording/listener acceptance attached |
| P2 world actions | Eat/drink/bandage/inject, equipment, locks/doors, place/upgrade/repair/demolish, crafting/research/recycler/fire, inventory attachment drag. | STILL OPEN — no recording/listener acceptance attached |
| P2 living world | Each animal calm/alert/threat/attack/hurt/death, gait and repeat intervals; each biome day/night, shore, inside/outside, changing weather when implemented. | STILL OPEN — no recording/listener acceptance attached |
| P2 ships/combat | Tin Finch/Iron Wake outside/deck/bridge; start/load/boost/release/shutdown/touchdown, cannons/loader/turrets, rockets/fire/explosion at distance. | STILL OPEN — no recording/listener acceptance attached |

## Available contact recordings to compare

Metadata and source hashes only: **none of these alternatives was listened to by this audit**. Selection below reflects the recording's stated material, not a claim that its timbre is better. All are already local and have repository provenance; no paid asset or download was used. The JSON retains exact filenames and source/archive hashes. Mud, leaves and gravel must keep their material identity rather than becoming an indiscriminate grass replacement.

| Source | Recorded description / clip | Duration | Disposition |
|---|---|---|---|
| BigSoundBank 0085 | Step on gravels | 7.89s | Candidate for audition; quality unassessed |
| BigSoundBank 0137 | Feet in Leaves #1 | 63.60s | Candidate for audition; quality unassessed |
| BigSoundBank 0495 | Steps in the Mud | 9.56s | Candidate for audition; quality unassessed |
| BigSoundBank 0510 | Footsteps on gravels #1 | 9.96s | Candidate for audition; quality unassessed |
| BigSoundBank 1117 | Footsteps on gravels #2 | 91.77s | Used by current footsteps; source subset and scoped grass gameplay feedback above; dirt runtime open |
| BigSoundBank 1301 | Steps on the Twigs | 15.59s | Candidate for audition; quality unassessed |
| BigSoundBank 2889 | Feet in leaves #2 | 65.45s | Used by current footsteps; source subset and scoped grass gameplay feedback above; dirt runtime open |
| BigSoundBank 3202 | Footsteps on Gravels #3 | 73.40s | Candidate for audition; quality unassessed |
| BigSoundBank 3216 | Footsteps on Gravels #4 | 23.62s | Prior baseline, replaced for grass/dirt |
| BigSoundBank 0854 | Steps in the short grass | 30.14s | Prior baseline, replaced for grass/dirt |
| BigSoundBank 1253 | Steps in the Grass, Slow | 23.40s | Prior baseline, replaced for grass/dirt |
| BigSoundBank 1843 | Running in the Tall Grass | 19.87s | Prior baseline, replaced for grass/dirt |
| TinyWorlds gravel.ogg | Different steps on wood, stone, leaves, gravel and mud | 0.70s | Candidate for audition; quality unassessed |
| TinyWorlds leaves01.ogg | Different steps on wood, stone, leaves, gravel and mud | 0.39s | Candidate for audition; quality unassessed |
| TinyWorlds leaves02.ogg | Different steps on wood, stone, leaves, gravel and mud | 0.47s | Candidate for audition; quality unassessed |
| TinyWorlds mud02.ogg | Different steps on wood, stone, leaves, gravel and mud | 0.26s | Candidate for audition; quality unassessed |
| TinyWorlds stone01.ogg | Different steps on wood, stone, leaves, gravel and mud | 0.29s | Candidate for audition; quality unassessed |

Other permitted local packs: Fantozzi's six sand contacts (0.30–0.44 s) and six stone contacts (0.32–0.37 s) are already used. Corsica_S has 42 snow-covered-gravel/ice contacts (0.44–0.72 s); their stated snowy material does not make them dry-ground recordings. Kenney Impact Sounds contains five `Audio/footstep_grass_000–004.ogg` clips (0.59–0.78 s), available for audition with no realism claim. All three source archive hashes were checked against `art/audio/sources.py` during this inventory. The local `Assets/Audio` folder remains empty; available packs are under `Assets/Downloads`.

## Whole catalogue coverage

[The machine-readable checklist](audio-action-checklist.json) contains one complete action row per event and one manifest row per variant. It retains the exact event, actual inspected trigger/timing, source segments, spatial and distance settings, variation, mix, observed defect, replacement disposition and scoped owner listening evidence for the four captured grass events. Item-role candidates are clearly separate from inspected trigger evidence. **Retain pending audition** is a review disposition, not approval of the current sound.

| Family | Events / variant references | Runtime route | Review focus |
|---|---|---|---|
| footstep | 32 / 144 | Local GroundAudio.UpdateOwnFeet and remote PeopleView feed FootstepDriver; cadence chooses gait and material. | Owner rejected the first thump-removal A/B reel. In a subsequent six-second comparison the owner confirmed the boink absent in the two processed source-contact sets (four exact BSB1117 dry and four exact BSB2889 organic contacts). This does not accept the in-game footsteps, new variants, timing or materials. Gravel shares dirt; exact prop/monument material remains incomplete. Two hosted grass movement captures passed technical checks. The owner confirmed the boink absent in gameplay-grass-mix.wav; this narrow result does not accept the second verification file or broader audio quality. |
| swim | 1 / 4 | Local GroundAudio.UpdateOwnFeet and remote PeopleView feed FootstepDriver; cadence chooses gait and material. | Owner rejected the first thump-removal A/B reel. In a subsequent six-second comparison the owner confirmed the boink absent in the two processed source-contact sets (four exact BSB1117 dry and four exact BSB2889 organic contacts). This does not accept the in-game footsteps, new variants, timing or materials. Gravel shares dirt; exact prop/monument material remains incomplete. |
| gather | 11 / 40 | Pickup events use GroundAudio.OnItems; natural-object hit/stage/depletion/weak-spot cues come from data through NatureView.Cue. | First-hit stall and contact/animation/audio alignment remain open; file onset alone cannot distinguish main-thread delay from waveform delay. |
| survival | 7 / 26 | Item use roles from held/UI actions; local DamageTaken/Died and cold timer; remote action/health changes in PeopleView. | All damage currently uses shared hurt voice. Food/water/medicine source suitability, drowning/immersion and remote sync need separate auditions. |
| campfire | 2 / 3 | ThingsView.Sound sees placed/working state; GroundAudio container open/close; role-based per item. | Check repeated model revisions for loop restarts, real recycler start/stop character, identical box lid aliases and interior occlusion. |
| furnace | 2 / 3 | ThingsView.Sound sees placed/working state; GroundAudio container open/close; role-based per item. | Check repeated model revisions for loop restarts, real recycler start/stop character, identical box lid aliases and interior occlusion. |
| fire | 4 / 9 | ThingsView.Sound sees placed/working state; GroundAudio container open/close; role-based per item. | Check repeated model revisions for loop restarts, real recycler start/stop character, identical box lid aliases and interior occlusion. |
| box | 2 / 6 | ThingsView.Sound sees placed/working state; GroundAudio container open/close; role-based per item. | Check repeated model revisions for loop restarts, real recycler start/stop character, identical box lid aliases and interior occlusion. |
| deploy | 1 / 4 | ThingsView.Sound sees placed/working state; GroundAudio container open/close; role-based per item. | Check repeated model revisions for loop restarts, real recycler start/stop character, identical box lid aliases and interior occlusion. |
| build | 12 / 36 | BuildView.Refresh snapshot piece additions/upgrades; GibsView destruction cause/material cues. Repair role requires consumer verification. | Repair and demolition role data alone do not prove playback; correlate exact accepted action with listed code/data references. |
| door | 8 / 18 | BuildView door open state; GroundView keypad press/answer; key lock role consumption must be confirmed separately. | No quality acceptance recorded; representative gameplay listening required. |
| lock | 1 / 2 | BuildView door open state; GroundView keypad press/answer; key lock role consumption must be confirmed separately. | No quality acceptance recorded; representative gameplay listening required. |
| codelock | 3 / 5 | BuildView door open state; GroundView keypad press/answer; key lock role consumption must be confirmed separately. | No quality acceptance recorded; representative gameplay listening required. |
| melee | 4 / 18 | FirstPersonView.Swing and remote action for swings; confirmed character melee hit uses hitFlesh; other hit roles require consumer evidence. | Hard-impact/item mapping and accepted-hit feedback must be checked per target. Character contact now resolves the confirmed Weapon id, preserving sound attribution after an equipment switch. |
| bow | 2 / 4 | Bow held control draw; ThingsView.ArrowSounds owns release/flight/end, using Shooter == Client.NetId for own accepted arrows. FirstPersonView.OnProjectile animates release only. | Duplicate release call removed from OnProjectile; one inspected release owner remains. Arrow flight is emitted at launch origin, not a following projectile. |
| arrow | 3 / 10 | Bow held control draw; ThingsView.ArrowSounds owns release/flight/end, using Shooter == Client.NetId for own accepted arrows. FirstPersonView.OnProjectile animates release only. | Duplicate release call removed from OnProjectile; one inspected release owner remains. Arrow flight is emitted at launch origin, not a following projectile. |
| rifle | 2 / 16 | Local LocalGun.ShotFired -> FirstPersonView.OnShot; remote PeopleView shot sequence; item shot/shotSilenced role. | Near/far balance, suppressed character, transient weight, recoil/audio alignment and burst masking require local/remote combat listening. |
| pistol | 2 / 15 | Local LocalGun.ShotFired -> FirstPersonView.OnShot; remote PeopleView shot sequence; item shot/shotSilenced role. | Near/far balance, suppressed character, transient weight, recoil/audio alignment and burst masking require local/remote combat listening. |
| reload | 6 / 16 | FirstPersonView.OnReload and PeopleView reload action; item magOut/magIn/rack roles. | No quality acceptance recorded; representative gameplay listening required. |
| gun | 2 / 6 | LocalGun.DryFired -> GroundAudio.OnDryFire -> weapon dryFire role. | Former catalogue-only role now has an inspected consumer; real empty pistol/rifle/launcher feel and input precedence need audition. |
| impact | 6 / 24 | ThingsView.Impact resolves bullet end surface; some props and gib-material mappings also reference these events. | Material distinction, armor/character overlay, multiple pellets and nearby wall masking need gameplay audition. No dedicated ricochet event appears in this catalogue. |
| hit | 3 / 5 | GroundAudio.OnHitMarker receives server-confirmed Character hits; kill overrides headshot, otherwise head region or body. | Existing distinct confirmations are present; perceptual prominence, armor/miss behavior, delay and masking remain unaccepted. |
| explosive | 2 / 2 | RaidView explosive state, fuse/beep timers, detonation and rocket state; round/item roles. | No quality acceptance recorded; representative gameplay listening required. |
| explosion | 2 / 10 | RaidView explosive state, fuse/beep timers, detonation and rocket state; round/item roles. | No quality acceptance recorded; representative gameplay listening required. |
| ship | 7 / 12 | ShipSounds.Update reads engine/load/boost/parts/hull/aboard state through data roles. | Original machinery synthesis/recorded composites need outside/deck/bridge audition, movement, turret competition, shutdown and distant checks. |
| ui | 10 / 21 | GroundUi.Sound delegates to GroundAudio; inventory/drag/drop and menu events; craft/rejection/chat/team/raid callbacks. | Check repeated drag/hover actions and gameplay notification masking; world-action result and UI gesture may intentionally layer, verify audibly. |
| amb | 12 / 17 | GroundAudio samples biome/forest/exposure/sea/sun -> AmbienceMix -> AmbienceDirector beds and timed accents. | GroundAudio Wind remains constant 0.3; weather/Storm input not connected to changing gameplay weather. Indoor/water attenuation and transitions need audition. |
| animal | 26 / 88 | AnimalView.Say(call) from replicated state/health/attack transitions; calm-call timer; gait phase for feet. | Species voices are original synthesis in animal_voices.py, not species field recordings; believable calls and transitions unaccepted. |
| iw | 27 / 74 | ShipSounds.Update reads engine/load/boost/parts/hull/aboard state through data roles. | Original machinery synthesis/recorded composites need outside/deck/bridge audition, movement, turret competition, shutdown and distant checks. |
| siege | 5 / 29 | StationsView shot report/mechanism/interior, loader Sound/EndSound phases in battery data, turret traverse and angular limit. | No quality acceptance recorded; representative gameplay listening required. |
| loader | 13 / 50 | StationsView shot report/mechanism/interior, loader Sound/EndSound phases in battery data, turret traverse and angular limit. | No quality acceptance recorded; representative gameplay listening required. |
| defense | 2 / 18 | StationsView shot report/mechanism/interior, loader Sound/EndSound phases in battery data, turret traverse and angular limit. | No quality acceptance recorded; representative gameplay listening required. |
| turret | 3 / 6 | StationsView shot report/mechanism/interior, loader Sound/EndSound phases in battery data, turret traverse and angular limit. | No quality acceptance recorded; representative gameplay listening required. |
| rocket | 3 / 15 | RaidView explosive state, fuse/beep timers, detonation and rocket state; round/item roles. | No quality acceptance recorded; representative gameplay listening required. |
| launcher | 4 / 12 | Local LocalGun.ShotFired -> FirstPersonView.OnShot; remote PeopleView shot sequence; item shot/shotSilenced role. | Near/far balance, suppressed character, transient weight, recoil/audio alignment and burst masking require local/remote combat listening. |
| recycler | 3 / 7 | ThingsView.Sound sees placed/working state; GroundAudio container open/close; role-based per item. | Check repeated model revisions for loop restarts, real recycler start/stop character, identical box lid aliases and interior occlusion. |

## Catalogue rows without an active consumer

These findings came from inspecting the actual routes, item mappings and battery tables, not merely a literal search (many working events are named dynamically). They remain open dispositions; dormant assets do not by themselves prove the underlying gameplay action is missing.

| Event | Finding |
|---|---|
| `survival.drink` | No item maps to this event and no gameplay consumer was found. Do not count the existence of a drinking recording as a working drinking action. |
| `build.repair` | STILL OPEN: repair sound has a catalogue role but no inspected gameplay consumer. Demolition/collapse are separately consumed by gibs cause data. |
| `melee.hit.hard` | Twelve melee item hitHard roles map here, but no role consumer was found. Natural gathering and bullet/prop impacts have other paths and do not prove this role works. |
| `gun.shell` | STILL OPEN: shell sound assets are present but not triggered by gameplay. |
| `iw.alarm.fire` | No item role or gameplay consumer found; ShipSounds.Alarm currently handles hull health only. |
| `loader.arm.swing.short` | No battery phase or gameplay consumer found in current data; other loader sounds are consumed through phase tables. |
| `fire.patch.small` | No current item fire role maps here; incendiary rocket maps to fire.patch.large. This is a dormant variant, not proof of missing small-fire simulation. |

## Evidence labels

- **Implemented route inspected:** named code/data path exists; this does not prove that a complete gameplay sequence reaches it correctly.
- **File integrity checked:** each current audio file is present and its bytes match its stored measurement manifest. Measurements are inherited build outputs, not newly measured by this audit.
- **Gameplay technically captured:** existing grass Master/Footsteps recordings, event traces and harness checks are retained and summarized. This audit did not capture or listen to them. Dirt and actual combat playback have no acceptance claim.
- **Visually/audibly reviewed:** the owner's exact source-sample and retained grass-mix boink-absence observations are attached. No agent audition, visual-timing review or owner audition of the second verification recording is claimed.
- **User accepted:** full-event quality remains false for every row. Four grass event rows carry separate scoped acceptance: the reported boink is absent in the retained gameplay-grass-mix.wav. This does not grant whole-audio quality acceptance.
- **Still open:** artistic mix/realism, material transitions, competition, action synchronization and every unrecorded scenario above.
