# Sound repair and source audit — 15 October 2026

R9 calls for a coherent library, correct foot contacts and an action audit. This is a bounded repair of the existing
recorded library and its runtime wiring, not a claim that every sound has received an artistic listening pass.

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
