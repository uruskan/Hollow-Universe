# Sound repair and source audit — 15 October 2026

R9 calls for a coherent library, correct foot contacts and an action audit. This is a bounded repair of the existing
recorded library and its runtime wiring, not a claim that every sound has received an artistic listening pass.

## Current surface and footwear revision — 18 October 2026

The older BSB grass/dirt contacts documented below were rejected as too tough and resembling bush traversal.
The owner approved softened Nox grass and dirty-ground previews as usable, though not perfect. The two previews
contained six independent recordings each. The expanded selection uses 193 Nox source contacts across all eight
surfaces. Each has three derived footwear profiles, giving 579 OGGs plus the four existing swim recordings.
The downloaded archive contains 689 footstep WAVs (479 Essentials, 210 Iceland); the selected banks are:

| Surface | Walk contacts | Run contacts | Land contacts |
|---|---|---|---|
| Grass | 16 | 10 | 6 |
| Dirt | 10 | 10 | 3 |
| Sand | 12 | 10 | 3 |
| Rock | 9 | 10 | 1 |
| Snow | 12 | 10 | 3 |
| Wood | 10 | 10 | 2 |
| Metal | 12 | 10 | 3 |
| Shallow water | 10 | 5 | 6 |

Crouch shares its surface's walk bank. Vegetation movement, jump takeoffs and clipped rock landing takes are excluded.
`art/audio/nox_footsteps.json` v2 pins the creator page, CC0 licence, archive SHA-256 and all 193 source-member hashes.
`sources.pack` extracts only that roster and verifies cached source bytes before use. The archive remains outside
the project. The expanded contacts have not received a listening approval; the owner's acceptance covers the
original twelve-contact softened previews.

`game/data/ground/footsteps.json` holds the shared contact treatment, footwear profiles and gait mapping. The common
treatment applies +6 dB before the audition's light EQ/compressor, then −0.5 dB. Only loudness outliers are adjusted
into −38.5 to −32 event LUFS, under a −8 dBTP ceiling; contacts already in that range retain their level differences.
Full contacts and short tail fades replace the old 0.32-second crop, with no synthetic bass impact. Profile filters
are baked into the OGGs, so there is no extra runtime filtering. The `footstep-contact` measurement category allows
−43 to −30 event LUFS after profile treatment.

Local and remote characters use the same resolver for surface, gait and the Feet item's `wear.footstepProfile`.
With no equipped footwear, it defaults to `barefoot`; `work-boots` selects `boots`. The registered `shoes` profile is available
for future footwear items to share, or an item can declare an additional registered profile without a per-item code
branch. Profile event gain is −2/0/+1 dB for barefoot/shoes/boots, followed by gait gain of 0/+3/−7/+4 dB for
walk/run/crouch/land. Existing cadence, landing-speed intensity, spatial attenuation, shuffle bags and small
pitch/volume variation remain in effect.

These are derived footwear treatments of real surface contacts. The source performer's footwear is unidentified;
the banks are not separately recorded authentic barefoot, shoe and boot sessions. Dedicated footwear recordings
can replace or extend these treatments later. Validation and engine-import results are recorded in `DEVELOPMENT.md`.

## Library and provenance

`D:/GameDev/Assets/Audio` is empty. The useful supplied library is in `D:/GameDev/Assets/Downloads`: Kenney Impact,
RPG, Interface, UI and Sci-fi packs; Fantozzi/Corsica/Tinyworlds/Rubberduck footsteps and material foley; HaelDB
voices; Still North Media's recorded firearm library; and 251 BigSoundBank recordings. The repository records these
as CC0 with URLs and SHA-256 values in `art/audio/sources.py`, `bigsoundbank.json` and `game/assets/THIRD_PARTY.md`.
`python -I art/audio/sources.py --verify` checked all **263 downloads, zero missing**, against those hashes.
No source originals were changed, no external download or paid generation was used, and no sounds were extracted
from a third-party game.

The inherited library already contains real recorded steps, gunfire, reloads, impacts, cloth, food, doors, wind/forest
recordings and voices. Original synthesis supplies mechanisms, rocket motors, alarms and animal calls; those are
identified in the credits. Replacing this whole library with a superficially different pack would not correct its
contact and action-routing defects.

Eight dedicated action events now replace three misleading sets of aliases, using **22 distinct new OGG files**:

| Action | Previous routing | New content |
|---|---|---|
| Injector use | Bandage tape | Three cap/catch, handling, plunger and release-click mixes |
| Hand launcher shot | Rifle shot | Three gas/backblast reports, separate from the projectile's motor |
| Launcher reload stages | Rifle magazine/bolt | Three variants each for opening, loading and locking the tube |
| Recycler operation | Furnace/fire | Three starts, three stops and a continuous gearbox/recorded steel-scrape loop |

`art/audio/repair_actions.py` builds only these outputs through the existing `build.py` measuring/encoding path.
The normal `events.everything()` includes the same recipes. The injector mixes CC0 foley; the launcher/recycler mix
CC0 field recordings with our original machinery synthesis. They are purpose-made mixes, not claims of recording
a real injector, launcher or industrial recycler. The full catalogue now has **235 events and 783 variants**.

All 22 new files passed SHA-256, unique-byte, 48 kHz encoding pipeline, loudness-range, peak ceiling, DC, leading
silence and tail checks. Their peaks range from -11.2 to -1.7 dBTP. Playback still uses the existing shuffle bags
without immediate repeats, bounded pitch/volume variation, attenuation and distance layering.

## Runtime corrections

- Local walking speed is decomposed in the character's movement frame: radial planet up or ship-local +Y. A ship's
  motion is not counted as walking, and ship-local coordinates are not sampled as points inside the planet.
- Contacts use actual zone-space feet. A short ray resolves a constructed floor's grade; ship decks resolve to metal.
  Natural terrain uses the same material families and actual slope as its surface cover. Material resampling happens
  within 0.6 m and every quarter-second locally, so crossing a floor edge does not retain several metres of old audio.
  A scanned cliff contact protruding from the shared height-field collision resolves to rock; monument footprints are
  excluded because their generated floors share those collision tiles too.
- Shallow inland lakes/streams now splash as well as the sea, using `ISurface.WaterLevelAt`. Interior frames are dry.
- Seats, noclip, dead/absent characters clear cadence. Airborne movement has no steps; genuine falls still have a landing.
  Clearing prevents a queued fall from sounding when standing after a seat/noclip transition. Swimming clears old fall speed.
- Remote seated characters reset their driver; remote deck movement continues to use relative velocity.
- Ambient sea/exposure probes now follow great-circle directions around the listener. The old flat X/Z points with Y=0
  sampled unrelated parts of a whole spherical world.

## Action audit and validation boundary

| Group | Existing runtime coverage / this pass |
|---|---|
| Walk/run/crouch/landing, shallow water, swim | All eight substrate sets exist; corrected contact/frame/cadence routing above |
| Melee, trees, ore, weak spots | Recorded swings and material impacts are wired; hit timing/performance remains separate |
| Bow draw/release/flight/impact | Draw is wired by held control; accepted arrow launch now drives own release animation and sound |
| Gun shots/reloads/dry fire/shells/impacts | Dedicated pistol/rifle recorded layers exist; launcher aliases replaced |
| Player hit/headshot/kill confirmation | Server HitMarker reaches GroundAudio and distinct concise events already exist; subjective mix still needs listening |
| Eat/drink/bandage/inject, damage/death/cold | Recorded survival foley/voice exists; injector now distinct |
| Doors/locks/build/repair/demolish/place | Existing material/stage events; no speculative blanket remapping |
| Fires/recycler/research | Existing state transitions/loops; recycler now machinery rather than fire |
| Inventory/craft/chat/refusals | Existing UI events; H/drag behavior separately validated by actual input smoke checks |
| Ships, batteries, rockets, explosions | Existing original layered machinery/weapon library; handheld launcher report now distinct |
| Animals | Calls are still original synthetic voices; field-recorded species replacements and audition remain future polish |

`AudioRepairTests` checks radial and ship-local velocity, seated/noclip suppression, silence in air, real landing,
cadence reset, shallow water, all constructed floor grades at three planet orientations, inland water, and dedicated
action routing with nonempty variant sets. `SoundCatalogueTests` checks every file/import and measured level/hash.
All four `AudioRepairTests` passed in the coordinated run (`.artifacts/repair-focused3.log`). The root agent coordinates
Godot import, catalogue tests and engine playback checks; see its run logs for those results.

The new files were imported, and the recycler's `.ogg.import` loop flag was set through `repair_actions`' recipes and
`build.fix_imports`; the coordinated final import must include that change. Engine audit: `--view sounds --sound-check`; audition an event with
`--view sounds --sound launcher.shot --play --trace`. `--play-all --trace` smoke-tests all events at multiple distances.

Not completed by this pass: a human listening pass on every event, species-specific recorded animal voices, every
natural prop's exact foot material, or a complete gunplay/animation redesign. Current automatic checks establish
correct routing and technically valid audio, not final subjective sound quality.

## Normal-ground "boink" and action feedback — 16 October 2026

The owner specifically rejected ordinary walking/jumping sounds. Removing the generic Kenney `impactSoft_heavy`
layer was insufficient: the owner heard the boink throughout the first A/B reel. That first comparison is rejected.
The recordings themselves retained tonal body, and landings combined two unrelated contacts. A second six-second
reel used four contacts each from BSB 1117 (dry gravel) and BSB 2889 (leaf cover), with controlled filtering and no
doubled contact. The owner replied **"Neither set has it"** when asked about the boink. That is acceptance of the
sample sets for this defect, not acceptance of the entire game mix or every material.

Those four selected contacts now drive dirt and grass walk/run/crouch; their first three supply single-contact
landings. Contact duration is capped at 0.32 s, without varispeed gait changes. Gait intensity still uses the existing
cadence/level and landing-force rules. Pitch variation narrows to 0.98–1.02 and jitter to 0.8 dB for these materials.
Dry contacts additionally receive gentle transient compression: one auditioned cut measured -30.1 LUFS, just below
the existing -30 minimum; the final cut is -28.5 with -1.9 dBTP. No validation range was weakened. Crouch retains a
12 ms onset fade and lower runtime level. These processing differences are separate from the approved source reel.

Against the starting checkpoint, **52 footstep variants changed, eight obsolete variants/imports were removed,
and 96 footstep/swim files are unchanged**. Other materials retain their original contact recordings with the
generic soft-heavy thump removed; their quality remains unaccepted. Metal retains its separate contact ring.
Shuffle bags, spatial attenuation, authority and movement routing are preserved. No source original changed.

`python -I art/audio/repair_footsteps.py` rebuilds only changed footsteps through the existing measured encoder.
The full `build.py` uses the same `events.py` recipes; its whole-catalogue recipe enumeration was stopped before
asset writes because it spends several minutes evaluating unrelated synthesis. The bounded command preserves
other catalogue entries, measurements and hand-tuned playback settings. All 263 source downloads verify.

Three adjacent feedback defects are repaired:

- `ThingsView` owns one bow release; `Shooter` identifies the local accepted arrow. The first-person view retains
  the release animation, avoiding its former extra sound alongside the spatial release.
- Accepted melee contact uses `HitMarkerEvent.Weapon`, so changing held items before confirmation cannot turn a
  rifle hit into a melee thump or suppress a genuine melee contact. Existing body/head/kill markers remain.
- Shared `LocalGun.DryFired` now consumes the existing data-driven `dryFire` role once per fresh empty trigger
  press. Held input, equip/reload/cooldown refusals, sprint/swim, seats, wounded/dead and disabled actions stay silent.
  Empty feedback creates no shot, recoil, ammunition change or fire intent.

The complete [action checklist](reports/audio-action-checklist.md) and its JSON enumerate 235 events / 775 files,
with actual inspected routes, mix/distance/variation, defects and review disposition. Exact source segments and
license/hash provenance are exported for all 148 footstep/swim outputs. Other outputs retain explicit recipe-section
pointers; their per-file source reconstruction and listening are still open. The audit found dormant/unconsumed
shell and successful-repair sounds, among other gaps; a file existing is not a working gameplay trigger.

### Reproduction and acceptance boundary

Run an ordinary hosted client with a fresh or copied save, unique port, sound enabled and:

```text
--view ground --host --name AudioReview --spawn origin --port 27386
--db <absolute-new-save-path> --tour <absolute-output-folder> --tour-steps audio-movement --sound-trace
```

`audio-movement` is explicitly opt-in. It sends normal predicted movement for idle/walk/run/crouch and three jumps,
records the unedited Godot Master and Footsteps buses as WAV, and saves timestamps, movement, sound events, bus
levels and start/end screenshots. It checks silent idle, displacement for each gait, exactly one landing per jump,
and recorded PCM activity/duration. Run alone: event logging includes every voice. Timestamps mark sound start
requests, not sample-accurate audible onset; audio buffering and spatial delay still apply. It does not
grant items, teleport, change settings or inject individual sounds. Its JSON always leaves `qualityAccepted=false`.
The Jolt regression independently checks ordinary spherical-ground walk/run and real jumps for landing chatter.

The sample A/B reel is generated by `art/audio/footstep_comparison.py` from preserved before/current outputs at
identical pitch/order/gain. It is labelled by timestamps and hashes and is **not gameplay**. The rejected first
reel and the later owner-reviewed source reel are retained separately. Hosted recordings supply actual playback
evidence; recording and technical checks alone do not establish an audible-quality verdict.

The first ordinary hosted grass run passed: 11.25 m walking, 21.81 m running, 5.20 m crouching and three individual
landing events. Idle emitted no footsteps. Its unedited [game mix](audio/repairs/2026-10-16/gameplay-grass-mix.wav),
[isolated feet](audio/repairs/2026-10-16/gameplay-grass-feet.wav) and
[trace](audio/repairs/2026-10-16/gameplay-grass.json) are retained. The 26.85/26.93 s WAVs contain nonzero audio with
no clipped PCM samples. The owner-reviewed [source reel](audio/repairs/2026-10-16/approved-source-contacts.wav)
is separately retained. The owner then confirmed **"okay gameplay-grass-mix.wav doesnt have it"**. The reported
boink is therefore accepted as absent in this actual grass walk/run/crouch/jump recording. This is not acceptance
of every material, the dry-contact runtime mix or the entire audio library.
Start/end screenshots were inspected for real ground, character movement and preserved HUD; they are not
environment-art acceptance. Existing exit warnings still report seven Texture RIDs and two ObjectDB instances.
The final recorder-guard repeat also passed: both buses captured 26.848 s against 26.858 s elapsed, with nonzero
PCM activity. Its raw files, trace and validation logs are in `audio/repairs/2026-10-16/verification`.

Current validation and retained gameplay evidence are recorded in DEVELOPMENT.md. Remaining quality work includes
other walk source auditions, all material boundaries and remote listeners, combat masking/timing, recorded animal
voices, unconsumed actions and the broader library mix. This batch does not finish R9/L19/L21 or all 47 reports.
