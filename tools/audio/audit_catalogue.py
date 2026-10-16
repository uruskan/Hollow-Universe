"""Inventory every current sound event without mistaking file checks for listening.

Run from any directory: python -I tools/audio/audit_catalogue.py
--footstep-recipes evaluates only the footstep builders for exact segment provenance.
--include-recipes evaluates the full library and can be slow and memory-intensive.
Both can populate art/.cache/audio and invoke ffmpeg for onset analysis, but do not
render game assets or rewrite the sound catalogue.

Generated reports are snapshots. Re-run after sound/routing edits. Listening evidence
belongs in a separate review record; this script never manufactures acceptance.
"""

from __future__ import annotations

import argparse
import ast
from collections import Counter
import hashlib
import inspect  # Preload stdlib before legacy art/audio/inspect.py enters sys.path.
import json
from pathlib import Path
import sys
import wave

ROOT = Path(__file__).resolve().parents[2]
AUDIO = ROOT / "art/audio"
REPORT = ROOT / "docs/reports/audio-action-checklist"
CATALOGUE = ROOT / "game/data/ground/sounds.json"
LEVELS = ROOT / "game/assets/audio/levels.json"
OWNER_FEEDBACK = dict(date="2026-10-16", evidence_kind="owner_listening_report",
    subject="First footstep A/B reel after removing generic thump layers",
    verdict="REJECTED; STILL_OPEN",
    observation="Owner reports the boink remains throughout the recording, mainly while walking and jumping. Removing generic thumps was insufficient; unchanged base contact recordings and their processing/routing need diagnosis.",
    boundary="This records the owner's reported audition, not an agent listening claim or a full catalogue review.")
SOURCE_SAMPLE_FEEDBACK = dict(date="2026-10-16", evidence_kind="owner_listening_report",
    subject="Six-second processed dry/organic contact comparison",
    path=".artifacts/audio-first/source-candidates-v2/new-ground-contacts.wav",
    retained_path="docs/audio/repairs/2026-10-16/approved-source-contacts.wav",
    sha256="e0399edbc23ff216dd77f22773d62828a15af65c01198e3e20856c3264b9f9b3",
    verdict="BOINK_ABSENT_IN_BOTH_SOURCE_SAMPLE_SETS; SOURCE_SAMPLE_SCOPE_ONLY",
    owner_words="Neither set has it",
    scope="Four exact selected contacts from BSB1117 and four from BSB2889, with the processing below; not the whole original recordings, new slices, extra variants or the in-game mix.",
    selection=dict(count=4, min_sep_seconds=0.45, min_len_seconds=0.05, max_len_seconds=0.3,
                   segment_extra_seconds=0.025, output_length_seconds=0.32, maximum_fade_seconds=0.07),
    processing=dict(dry="highpass 180 Hz; EQ 900 Hz, width 2 octaves, -9 dB; lowpass 7500 Hz",
                    organic="highpass 180 Hz; lowpass 6500 Hz"),
    boundary="User confirmed absence of the reported boink in this sample only. Grass/dirt gameplay, gait processing, repetition, timing, landings, other materials and the wider mix remain open.")
GRASS_GAMEPLAY_FEEDBACK = dict(date="2026-10-16", evidence_kind="owner_listening_report",
    path="docs/audio/repairs/2026-10-16/gameplay-grass-mix.wav",
    owner_words="okay gameplay-grass-mix.wav doesnt have it",
    verdict="BOINK_ABSENCE_ACCEPTED_IN_RETAINED_GRASS_GAMEPLAY_MIX",
    scope="Absence of the reported boink in this exact grass gameplay mix, including its captured walking, running, crouching and landings.",
    boundary="Does not accept the second technical verification recording, dirt runtime audio, other materials, transitions, broader timbre/mix quality or the whole sound catalogue.")
FOOTSTEP_INTEGRATION = dict(date="2026-10-16", status="IMPLEMENTED; RETAINED_GRASS_GAMEPLAY_BOINK_ABSENCE_ACCEPTED; DIRT_CAPTURE_OPEN",
    materials=dict(grass="BSB2889 leaf cover", dirt="BSB1117 dry gravel"),
    source_selection="The four exact source contacts selected for the owner-auditioned reel; n=4 unchanged.",
    variants_per_material=dict(walk=4, run=4, crouch=4, land=3),
    gait_changes="Walk/run reuse the same contacts with cadence/level changes; crouch uses a 12 ms fade; land uses one of the first three contacts without doubling.",
    pitch_range=[0.98, 1.02], volume_jitter_db=0.8,
    processing_difference_from_audition="Dry/dirt contacts additionally use a gentle compressor: threshold -15 dB, ratio 1.5, attack 0.2 ms, release 25 ms, makeup 1, knee 2. This post-audition processing has no user gameplay acceptance yet.",
    measurement_reason="Dry source contact 3 was approximately -30.1 LUFS, below the existing category minimum; transient control reaches approximately -28.5 LUFS with -1.9 dBTP. Existing loudness/peak bounds were not relaxed.",
    acceptance_boundary="The owner confirmed boink absence in the source sample and the retained gameplay-grass-mix.wav. Two grass captures passed technical checks, but no owner audition of the second verification recording is claimed. Dirt gameplay, the extra dry compression, material transitions and wider mix remain unaccepted.")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def relative(path):
    try:
        return Path(path).resolve().relative_to(ROOT).as_posix()
    except ValueError:
        return Path(path).as_posix()


def gameplay_evidence():
    """Summarize existing captures; this audit neither runs the game nor listens."""
    results = []
    recordings = [
        ("retained_grass_capture", ROOT / "docs/audio/repairs/2026-10-16/gameplay-grass.json", "gameplay-grass"),
        ("hardened_grass_repeat", ROOT / ".artifacts/audio-first/gameplay-final/audio-movement.json", "audio-movement"),
    ]
    for label, path, stem in recordings:
        if not path.exists():
            continue
        saved = json.loads(path.read_text(encoding="utf-8-sig"))
        counts = Counter((e["stage"], e["key"]) for e in saved["events"])
        captures = []
        for bus in ("mix", "feet"):
            wav = path.parent / f"{stem}-{bus}.wav"
            if not wav.exists():
                continue
            with wave.open(str(wav)) as header:
                captures.append(dict(bus=bus, path=relative(wav), sha256=digest(wav),
                    duration_seconds=header.getnframes() / header.getframerate(),
                    sample_rate=header.getframerate(), channels=header.getnchannels(),
                    sample_width_bytes=header.getsampwidth()))
        results.append(dict(label=label, trace=relative(path), trace_sha256=digest(path),
            technical_checks_passed=saved["technicalChecksPassed"], failure=saved.get("failure"),
            harness_quality_accepted_at_capture=saved["qualityAccepted"],
            user_listening_status=("BOINK_ABSENCE_ACCEPTED_IN_THIS_MIX_RECORDING" if label == "retained_grass_capture" else "NO_OWNER_AUDITION_CLAIM"),
            user_feedback=GRASS_GAMEPLAY_FEEDBACK if label == "retained_grass_capture" else None,
            evidence=saved["evidence"],
            timing_boundary=saved.get("eventTiming", "Events timestamp sound-start requests, not sample-accurate audible onset; audio buffering still applies."),
            surfaces=sorted({e["pose"]["surface"] for e in saved["events"]}),
            event_counts=[dict(stage=stage, event=event, count=count) for (stage, event), count in sorted(counts.items())],
            wav_headers=captures, harness_capture_checks=saved.get("captures", []),
            audit_performed_capture=False, auditioned_by_this_audit=False))
    return results


def location(path, needle):
    for line, text in enumerate((ROOT / path).read_text(encoding="utf-8-sig").splitlines(), 1):
        if needle in text:
            return f"{path}:{line}"
    return path + " (anchor missing; re-inspect)"


def route(key):
    """Human-inspected family paths; literal/data references supplement these below."""
    paths = []
    finding = "No quality acceptance recorded; representative gameplay listening required."
    decision = "Retain pending audition; replace only an identified weak source or incorrect action mapping."
    status = "ROUTE_INSPECTED; AUDIBLE_QUALITY_OPEN"
    if key.startswith("footstep.") or key == "swim.stroke":
        trigger = "Local GroundAudio.UpdateOwnFeet and remote PeopleView feed FootstepDriver; cadence chooses gait and material."
        timing = "Distance-integrated cadence; landing after >=0.12 s airborne and >=2.2 m/s fall. No separate jump-takeoff event. Local -3 dB; landing adds speed gain."
        paths = [("game/scripts/Ground/GroundAudio.cs", "private void UpdateOwnFeet"), ("src/Hollow.Client/Audio/SoundRules.cs", "public FootEvent? Update"), ("game/scripts/Audio/FootstepDriver.cs", "public void Update")]
        finding = "Owner rejected the first thump-removal A/B reel. In a subsequent six-second comparison the owner confirmed the boink absent in the two processed source-contact sets (four exact BSB1117 dry and four exact BSB2889 organic contacts). This does not accept the in-game footsteps, new variants, timing or materials. Gravel shares dirt; exact prop/monument material remains incomplete."
        decision = "PRIORITY 1: grass/dirt now use those four source contacts, with a documented extra dry-contact compressor and gait processing. Obtain gameplay/user listening evidence across gaits/landings. Do not infer acceptance of other materials."
        status = "SOURCE_SAMPLE_FEEDBACK_RECORDED; GAMEPLAY_QUALITY_OPEN"
        if key.endswith(".grass"):
            finding += " Two hosted grass movement captures passed technical checks. The owner confirmed the boink absent in gameplay-grass-mix.wav; this narrow result does not accept the second verification file or broader audio quality."
            decision = "Preserve the grass boink fix accepted in the retained recording; compare dirt runtime, other materials, transitions and broader mix quality separately."
            status = "RETAINED_GRASS_GAMEPLAY_BOINK_ABSENCE_ACCEPTED; BROADER_QUALITY_OPEN"
    elif key.startswith("hit."):
        trigger = "GroundAudio.OnHitMarker receives server-confirmed Character hits; kill overrides headshot, otherwise head region or body."
        timing = "Authoritative confirmation arrival, not predicted muzzle/swing time; flat playback for shooter."
        paths = [("game/scripts/Ground/GroundAudio.cs", "private void OnHitMarker")]
        finding = "Existing distinct confirmations are present; perceptual prominence, armor/miss behavior, delay and masking remain unaccepted."
        decision = "PRIORITY 1: audition accepted body/head/kill, misses and armored hits during competing gunfire before tuning."
    elif key.startswith("gather."):
        trigger = "Pickup events use GroundAudio.OnItems; natural-object hit/stage/depletion/weak-spot cues come from data through NatureView.Cue."
        timing = "Pickups on ItemsChanged(PickedUp); natural feedback on replicated state changes with configured SoundDelay and weak-spot pitch."
        paths = [("game/scripts/Ground/GroundAudio.cs", "private void OnItems"), ("game/scripts/Ground/NatureView.cs", "private void Cue")]
        finding = "First-hit stall and contact/animation/audio alignment remain open; file onset alone cannot distinguish main-thread delay from waveform delay."
    elif key.startswith("survival."):
        trigger = "Item use roles from held/UI actions; local DamageTaken/Died and cold timer; remote action/health changes in PeopleView."
        timing = "Use follows action dispatch; damage/death confirmation; cold every 4–7 s when Cold >0.5. Compare use transient with animation contact."
        paths = [("game/scripts/Ground/GroundAudio.cs", "private void OnDamage"), ("game/scripts/Ground/Held/HeldControls.cs", "Sounds.Flat(Sounds.Item(item.Key, option.Action.Key)"), ("game/scripts/Ground/PeopleView.cs", 'Sounds.At("survival.hurt"')]
        finding = "All damage currently uses shared hurt voice. Food/water/medicine source suitability, drowning/immersion and remote sync need separate auditions."
    elif key == "gun.dryfire":
        trigger = "LocalGun.DryFired -> GroundAudio.OnDryFire -> weapon dryFire role."
        timing = "One empty-weapon outcome on pressed trigger; holding automatic fire does not repeatedly click; reload/equip/cadence rejection is excluded."
        paths = [("src/Hollow.Client/LocalGun.cs", "pressed && fire == OutcomeCode.Empty"), ("game/scripts/Ground/GroundAudio.cs", "private void OnDryFire")]
        finding = "Former catalogue-only role now has an inspected consumer; real empty pistol/rifle/launcher feel and input precedence need audition."
        decision = "Preserve shared empty-trigger route; verify each weapon, held automatic input, reload and equip transitions."
    elif key == "gun.shell":
        trigger = "Catalogue pistol/rifle shell roles exist; no gameplay shell role consumer found in inspected scripts."
        timing = "No verified ejection/contact timing."
        paths = [("game/scripts/Ground/FirstPersonView.cs", "private void OnShot")]
        finding = "STILL OPEN: shell sound assets are present but not triggered by gameplay."
        decision = "Add shared ejection/contact and surface behavior; avoid arbitrary shell sound timing on every muzzle report."
        status = "ROUTE_GAP; AUDIBLE_QUALITY_OPEN"
    elif key.startswith(("rifle.shot", "pistol.shot")) or key == "launcher.shot":
        trigger = "Local LocalGun.ShotFired -> FirstPersonView.OnShot; remote PeopleView shot sequence; item shot/shotSilenced role."
        timing = "Local predicted accepted shot at muzzle; remote replicated shot. Distance report layers can add propagation delay."
        paths = [("game/scripts/Ground/FirstPersonView.cs", "private void OnShot"), ("game/scripts/Ground/PeopleView.cs", 'Sounds.At(Sounds.Item(gun.Key, silenced')]
        finding = "Near/far balance, suppressed character, transient weight, recoil/audio alignment and burst masking require local/remote combat listening."
    elif key.startswith("reload.") or key in ("launcher.open", "launcher.load", "launcher.lock"):
        trigger = "FirstPersonView.OnReload and PeopleView reload action; item magOut/magIn/rack roles."
        timing = "Start, 55% and 85% of reload duration; timers gate equipped/reloading state. Compare to actual held clip rather than assuming frame sync."
        paths = [("game/scripts/Ground/FirstPersonView.cs", "private void OnReload"), ("game/scripts/Ground/PeopleView.cs", "A reload's parts")]
    elif key.startswith("melee."):
        trigger = "FirstPersonView.Swing and remote action for swings; confirmed character melee hit uses hitFlesh; other hit roles require consumer evidence."
        timing = "Swing starts with shared melee action; flesh confirmation arrives from server. Natural targets use separate shared gather cue path."
        paths = [("game/scripts/Ground/FirstPersonView.cs", "public void Swing"), ("game/scripts/Ground/GroundAudio.cs", "private void OnHitMarker"), ("game/scripts/Ground/NatureView.cs", "private void Cue")]
        finding = "Hard-impact/item mapping and accepted-hit feedback must be checked per target. Character contact now resolves the confirmed Weapon id, preserving sound attribution after an equipment switch."
    elif key.startswith(("bow.", "arrow.")):
        trigger = "Bow held control draw; ThingsView.ArrowSounds owns release/flight/end, using Shooter == Client.NetId for own accepted arrows. FirstPersonView.OnProjectile animates release only."
        timing = "Draw at aim transition; release on accepted arrow; impact on projectile end."
        paths = [("game/scripts/Ground/Held/HeldControls.cs", '"bow.draw"'), ("game/scripts/Ground/FirstPersonView.cs", "private void OnProjectile"), ("game/scripts/Ground/ThingsView.cs", "private void ArrowSounds")]
        finding = "Duplicate release call removed from OnProjectile; one inspected release owner remains. Arrow flight is emitted at launch origin, not a following projectile."
        decision = "Verify single-launch trace and actual local/remote draw cancel, release, flight and flesh/hard contact."
    elif key.startswith("impact."):
        trigger = "ThingsView.Impact resolves bullet end surface; some props and gib-material mappings also reference these events."
        timing = "At replicated projectile end or prop health change; spatial at contact."
        paths = [("game/scripts/Ground/ThingsView.cs", "private void Impact"), ("game/scripts/Ground/ThingsView.cs", "private void Prop")]
        finding = "Material distinction, armor/character overlay, multiple pellets and nearby wall masking need gameplay audition. No dedicated ricochet event appears in this catalogue."
    elif key.startswith("build."):
        trigger = "BuildView.Refresh snapshot piece additions/upgrades; GibsView destruction cause/material cues. Repair role requires consumer verification."
        timing = "On replicated piece/state or Destroyed cue, spatial at piece."
        paths = [("game/scripts/Ground/BuildView.cs", 'Sounds.At($"build.place.'), ("game/scripts/Ground/GibsView.cs", "broke.CauseSound")]
        finding = "Repair and demolition role data alone do not prove playback; correlate exact accepted action with listed code/data references."
        if key == "build.repair":
            trigger = "HammerControl sends repair through BuildView.Use; catalogue hammer repair role exists, but no sound consumer found for successful repairs."
            timing = "No verified successful-repair audio timing."
            finding = "STILL OPEN: repair sound has a catalogue role but no inspected gameplay consumer. Demolition/collapse are separately consumed by gibs cause data."
            decision = "Add confirmed repair feedback through shared action/result presentation; keep rejected repair silent except existing refusal cue."
            status = "ROUTE_GAP; AUDIBLE_QUALITY_OPEN"
    elif key.startswith(("door.", "lock.", "codelock.")):
        trigger = "BuildView door open state; GroundView keypad press/answer; key lock role consumption must be confirmed separately."
        timing = "Door state change / keypad click / authoritative keypad answer; generic world door slam may compete."
        paths = [("game/scripts/Ground/BuildView.cs", "Sounds.Item(door.Def.Key"), ("game/scripts/Ground/GroundView.cs", "_keypad.Pressed")]
    elif key.startswith(("campfire.", "furnace.", "recycler.")) or key in ("fire.extinguish", "box.open", "box.close", "deploy.place"):
        trigger = "ThingsView.Sound sees placed/working state; GroundAudio container open/close; role-based per item."
        timing = "State revision starts/stops a loop on model node; model rebuild restarts it. Container sounds on accepted open/close callbacks."
        paths = [("game/scripts/Ground/ThingsView.cs", "private void Sound"), ("game/scripts/Ground/GroundAudio.cs", "private void OnOpened")]
        finding = "Check repeated model revisions for loop restarts, real recycler start/stop character, identical box lid aliases and interior occlusion."
    elif key.startswith(("explosive.", "explosion", "rocket.")):
        trigger = "RaidView explosive state, fuse/beep timers, detonation and rocket state; round/item roles."
        timing = "Fuse follows entity, beep countdown, authoritative detonation; report distance layers/delay; rocket loop follows projectile node."
        paths = [("game/scripts/Ground/RaidView.cs", "shown.FuseSound = Sounds.Loop"), ("game/scripts/Ground/RaidView.cs", 'Sounds.At(Sounds.Item(item, "explode")')]
    elif key.startswith("fire."):
        trigger = "FiresView burning area state and item fire/fireStart roles; loop follows attached fire node."
        timing = "Started/ended state; loop fades out on ending."
        paths = [("game/scripts/Ground/FiresView.cs", 'Sound = Sounds.Loop')]
    elif key.startswith(("ship.", "iw.")):
        trigger = "ShipSounds.Update reads engine/load/boost/parts/hull/aboard state through data roles."
        timing = "Continuous engine crossfades; starts/stops and moving-part phase crossings; hull and alarm transitions."
        paths = [("game/scripts/Ships/ShipSounds.cs", "private string? Role"), ("game/scripts/Ground/ShipsView.cs", "d.Sounds.Update")]
        finding = "Original machinery synthesis/recorded composites need outside/deck/bridge audition, movement, turret competition, shutdown and distant checks."
    elif key.startswith(("siege.", "defense.", "loader.", "turret.")):
        trigger = "StationsView shot report/mechanism/interior, loader Sound/EndSound phases in battery data, turret traverse and angular limit."
        timing = "Replicated shot/loader phase; skip catch-up/late join behavior must be auditioned; traverse follows angular speed."
        paths = [("game/scripts/Ships/StationsView.cs", "private void HearPhases"), ("game/scripts/Ships/StationsView.cs", 'Sounds.At(battery.Key + ".fire.report"')]
    elif key.startswith("animal."):
        role_name = key.rsplit(".", 1)[1]
        trigger = f"AnimalView.Say({role_name}) from replicated state/health/attack transitions; calm-call timer; gait phase for feet."
        timing = "Step: clip phase crossing (4 walk / 2 trot/run); calls randomized species intervals; voices on state changes."
        paths = [("game/scripts/Animals/AnimalView.cs", "private void Say"), ("game/scripts/Animals/AnimalView.cs", "private void Steps"), ("game/scripts/Animals/AnimalView.cs", "private void Voice")]
        if role_name != "step":
            finding = "Species voices are original synthesis in animal_voices.py, not species field recordings; believable calls and transitions unaccepted."
            decision = "Replace weak synthetic calls with licensed species-specific recordings; preserve state gates and variant budgets."
    elif key.startswith("amb."):
        trigger = "GroundAudio samples biome/forest/exposure/sea/sun -> AmbienceMix -> AmbienceDirector beds and timed accents."
        timing = "Place probe every 3 s, mix every 0.25 s; bed glide 1.5 s; random owl/crow/thunder accents around listener."
        paths = [("game/scripts/Ground/GroundAudio.cs", "private void UpdateAmbience"), ("game/scripts/Audio/AmbienceDirector.cs", "public void Update")]
        finding = "GroundAudio Wind remains constant 0.3; weather/Storm input not connected to changing gameplay weather. Indoor/water attenuation and transitions need audition."
    elif key.startswith("ui."):
        trigger = "GroundUi.Sound delegates to GroundAudio; inventory/drag/drop and menu events; craft/rejection/chat/team/raid callbacks."
        timing = "UI gesture or authoritative result, varying by specific call site; flat playback."
        paths = [("game/scripts/Ground/GroundAudio.cs", "GroundUi.Sound ="), ("game/scripts/UI/GroundUi.Inventory.cs", 'Sound?.Invoke("ui.drag")')]
        finding = "Check repeated drag/hover actions and gameplay notification masking; world-action result and UI gesture may intentionally layer, verify audibly."
    else:
        trigger = "UNRESOLVED: follow literal/data references below; no inspected family route."
        timing = "UNRESOLVED"
        status = "ROUTE_UNRESOLVED; AUDIBLE_QUALITY_OPEN"
    dormant = {
        "survival.drink": "No item maps to this event and no gameplay consumer was found. Do not count the existence of a drinking recording as a working drinking action.",
        "melee.hit.hard": "Twelve melee item hitHard roles map here, but no role consumer was found. Natural gathering and bullet/prop impacts have other paths and do not prove this role works.",
        "iw.alarm.fire": "No item role or gameplay consumer found; ShipSounds.Alarm currently handles hull health only.",
        "loader.arm.swing.short": "No battery phase or gameplay consumer found in current data; other loader sounds are consumed through phase tables.",
        "fire.patch.small": "No current item fire role maps here; incendiary rocket maps to fire.patch.large. This is a dormant variant, not proof of missing small-fire simulation.",
    }
    if key in dormant:
        trigger = "CATALOGUE ONLY / NO CURRENT CONSUMER FOUND: " + dormant[key]
        timing = "No active timing established for this particular event."
        finding = dormant[key]
        decision = "Resolve intended action/mapping before tuning; preserve as unused content or wire through the shared appropriate system."
        status = "CATALOGUE_ONLY; AUDIBLE_QUALITY_OPEN"
    return dict(trigger=trigger, timing=timing, runtime_evidence=[location(*p) for p in paths],
                status=status, observed_defect_or_risk=finding, replacement_decision=decision)


def source_sections():
    text = (AUDIO / "events.py").read_text(encoding="utf-8-sig")
    return {n.name: f"art/audio/events.py:{n.lineno}" for n in ast.parse(text).body if isinstance(n, ast.FunctionDef)}


def recipe_section(key, sections):
    prefix = key.split(".")[0]
    names = {"footstep": "footsteps", "swim": "footsteps", "gather": "gathering", "survival": "survival",
             "campfire": "deployables", "furnace": "deployables", "box": "deployables", "deploy": "deployables",
             "build": "building", "door": "building", "lock": "building", "codelock": "building",
             "melee": "weapons", "bow": "weapons", "arrow": "weapons", "rifle": "weapons", "pistol": "weapons",
             "reload": "weapons", "gun": "weapons", "impact": "weapons", "hit": "weapons",
             "explosive": "explosives", "explosion": "explosives", "ship": "ships", "ui": "interface",
             "amb": "ambience", "animal": "animals", "iw": "iron_wake", "siege": "_iw_siege", "loader": "_iw_loader",
             "defense": "_iw_defense", "turret": "_iw_turret", "rocket": "_iw_rockets", "fire": "_iw_fire"}
    if prefix in ("recycler", "launcher") or key == "survival.inject":
        return location("art/audio/repair_actions.py", "def recipes")
    if key == "fire.extinguish":
        return sections["deployables"]
    return sections.get(names.get(prefix, ""), "UNRESOLVED")


def all_references():
    refs = []
    # Exclude tests/viewers: a test or catalogue audition is not a gameplay trigger.
    for folder, suffix in [("game/scripts", ".cs"), ("src/Hollow.Client", ".cs"), ("game/data/ground", ".json")]:
        for path in sorted((ROOT / folder).rglob("*" + suffix)):
            if any(word in path.name for word in ("Viewer", "Sandbox", "Tour", "Preview")) or path == CATALOGUE:
                continue
            for n, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
                line = line.strip()
                if line.startswith(("//", "*")):
                    continue
                if '"' in line:
                    refs.append((relative(path) + f":{n}", line))
    return refs


def recipe_provenance(scope):
    sys.path.insert(0, str(AUDIO))
    import events
    import sources

    if scope == "footsteps":
        events.footsteps()
        outs = list(events.OUTS)
    else:
        outs, _ = events.everything()
    result = {}

    def source(path):
        path = Path(path).resolve()
        cache = Path(sources.CACHE).resolve()
        downloads = Path(sources.DOWNLOADS).resolve()
        if path.is_relative_to(cache):
            parts = path.relative_to(cache).parts
            if parts[0] in sources.PACKS:
                pack = sources.PACKS[parts[0]]
                return dict(kind="licensed_pack_member", pack=parts[0], member="/".join(parts[1:]),
                            author=pack.author, license=pack.licence, page=pack.page, archive_sha256=pack.sha256)
        if path.is_relative_to(downloads) and path.parent.name == "bigsoundbank":
            sid = path.stem
            item = sources.BIGSOUNDBANK.get(sid, {})
            return dict(kind="licensed_recording", collection="BigSoundBank", id=sid, title=item.get("title"),
                        license="CC0 1.0", page=f"https://bigsoundbank.com/detail-{sid}.html", source_sha256=item.get("sha256"))
        if "/art/.cache/audio/synth/" in path.as_posix():
            return dict(kind="generated_intermediate", path=relative(path), sha256=digest(path),
                        note="Original synthesis or processed recording composite. Recipe builder is authoritative for internal components; not a claim of an authentic field recording.")
        return dict(kind="unclassified_source", path=relative(path), note="Inspect provenance before replacement or distribution.")

    for out in outs:
        segments = []
        for seg in out.segs:
            segment = dict(source=source(seg.src), start_seconds=seg.start, duration_seconds=seg.dur,
                           gain_db=seg.gain, pitch=seg.pitch, at_seconds=seg.at, filters=seg.fx,
                           reverse=seg.reverse, fade_in=seg.fade_in, fade_out=seg.fade_out)
            if seg.ir:
                segment["impulse_response"] = source(seg.ir)
            segments.append(segment)
        result[out.rel] = dict(segments=segments, output_filters=out.fx, channels=out.channels,
                               recipe_hash=out.recipe())
    return result


SCENARIOS = [
    ("P0 normal ground", "Grass/dirt walk, run, crouch, jump takeoff/land, repeated jumps, small ledge and sprint-stop. Record event trace and real output together."),
    ("P0 state gates", "Airborne, sleeping/waking, seated/unseated, dead/respawn, noclip on/off; moving ship stationary feet and walking deck."),
    ("P0 materials", "Grass, soil, gravel, sand, stone, snow; twig/wood/stone/metal/armoured floors; floor edges, scanned cliffs, monument interior; lake/stream shallows and swim transitions."),
    ("P1 confirmations", "Own and remote body/head/kill, armored hit, miss, melee hit; quiet and competing gunfire/explosion. Correlate accepted hit tick, animation contact, playback start and perceived click."),
    ("P1 weapons", "Bow draw cancel/release/end, rifle/pistol/launcher shot, dry trigger, reload cancel, shells/ricochet; near/mid/far listener and blocked/unblocked line."),
    ("P1 gathering", "Tree/ore first hit, repeat hit, weak spot, stage change and depletion; correlate frame spike and waveform onset."),
    ("P2 world actions", "Eat/drink/bandage/inject, equipment, locks/doors, place/upgrade/repair/demolish, crafting/research/recycler/fire, inventory attachment drag."),
    ("P2 living world", "Each animal calm/alert/threat/attack/hurt/death, gait and repeat intervals; each biome day/night, shore, inside/outside, changing weather when implemented."),
    ("P2 ships/combat", "Tin Finch/Iron Wake outside/deck/bridge; start/load/boost/release/shutdown/touchdown, cannons/loader/turrets, rockets/fire/explosion at distance."),
]


def candidate_sources(recipes):
    """Local source metadata/hashes only; never decode, transform or audition audio."""
    sys.path.insert(0, str(AUDIO))
    import sources
    entries = []
    human_sources = [segment["source"] for path, recipe in recipes.items() if path.startswith("footstep/")
                     for segment in recipe["segments"]]
    used_bsb = {source["id"] for source in human_sources if source.get("collection") == "BigSoundBank"}
    used_members = {(source.get("pack"), source.get("member")) for source in human_sources}
    for sid in ("0085", "0137", "0495", "0510", "1117", "1301", "2889", "3202", "3216", "0854", "1253", "1843"):
        metadata = sources.BIGSOUNDBANK[sid]
        path = Path(sources.DOWNLOADS) / "bigsoundbank" / (sid + ".wav")
        duration = None
        if path.exists():
            with wave.open(str(path)) as f:
                duration = round(f.getnframes() / f.getframerate(), 3)
        entries.append(dict(source="BigSoundBank " + sid, title=metadata["title"], path=path.as_posix(),
            duration_seconds=duration, duration_method="WAV header", license="CC0 1.0 (repository source manifest)",
            source_sha256=metadata["sha256"], source_hash_matches=path.exists() and digest(path) == metadata["sha256"],
            already_used_in_current_human_steps=sid in used_bsb if recipes else None,
            used_before_current_base_contact_replacement=sid in ("3216", "0854", "1253", "1843"),
            owner_confirmed_boink_absence_in_specific_processed_subset=sid in ("1117", "2889"),
            auditioned_by_this_audit=False))
    clips = [("gravel.ogg", 0.70), ("leaves01.ogg", 0.39), ("leaves02.ogg", 0.47), ("mud02.ogg", 0.26), ("stone01.ogg", 0.29)]
    pack_name = "oga-tinyworlds-different-steps"
    metadata = sources.PACKS[pack_name]
    archive = Path(sources.DOWNLOADS) / pack_name / metadata.file
    matches = archive.exists() and digest(archive) == metadata.sha256
    for member, duration in clips:
        path = Path(sources.CACHE) / pack_name / member
        entries.append(dict(source="TinyWorlds " + member, title=metadata.title, path=path.as_posix(),
            duration_seconds=duration, duration_method="ffmpeg input header, rounded to 0.01 s; inspected 16 October 2026", license=metadata.licence,
            author=metadata.author, source_page=metadata.page, archive_sha256=metadata.sha256,
            archive_hash_matches=matches, extracted_file_sha256=digest(path) if path.exists() else None,
            already_used_in_current_human_steps=(pack_name, member) in used_members if recipes else None,
            auditioned_by_this_audit=False))
    return entries


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    recipe_options = parser.add_mutually_exclusive_group()
    recipe_options.add_argument("--include-recipes", action="store_true")
    recipe_options.add_argument("--footstep-recipes", action="store_true")
    args = parser.parse_args()
    catalogue = json.loads(CATALOGUE.read_text(encoding="utf-8-sig"))
    levels = json.loads(LEVELS.read_text(encoding="utf-8-sig"))["files"]
    recipe_scope = "all" if args.include_recipes else "footsteps" if args.footstep_recipes else "none"
    recipes = recipe_provenance(recipe_scope) if recipe_scope != "none" else {}
    gameplay = gameplay_evidence()
    references = all_references()
    sections = source_sections()
    entries = []
    files = set()
    for event in catalogue["events"]:
        key = event["key"]
        roles = [dict(item=item, role=role) for item, mapping in catalogue.get("items", {}).items()
                 for role, mapped in mapping.items() if mapped == key]
        entry = dict(event=key, **route(key))
        entry["catalogue"] = event
        entry["item_roles"] = roles
        entry["literal_references"] = [dict(location=loc, code=line) for loc, line in references if f'"{key}"' in line]
        # Candidate role references are intentionally not proof that every mapped item takes that route.
        role_names = {item["role"] for item in roles}
        entry["role_consumer_candidates"] = [dict(location=loc, code=line) for loc, line in references
            if loc.split(":", 1)[0].endswith(".cs") and any(f'"{role}"' in line for role in role_names)
            and any(token in line for token in ("Sounds.", "Role(", "ItemEvent(", ".Sound"))]
        entry["recipe_section"] = recipe_section(key, sections)
        entry["spatial_behavior"] = dict(space=event.get("space", "3d"), unit_size_m=event.get("unitSize", 4),
            maximum_distance_m=event.get("maxDistance", 60), occlusion=event.get("occlude", True),
            propagation_delay=event.get("delay", False), layers=event.get("layers", []),
            note="Call-site Play2D overrides positional data for local body/UI. 3D inverse-distance attenuation, occlusion ray mask1; layered reports use per-layer distance/volume. Timing text records known local overrides.")
        entry["variation_mix"] = dict(bus=event.get("bus", "SFX"), volume_db=event.get("volume", 0),
            pitch=event.get("pitch", [1, 1]), jitter_db=event.get("jitter", 0), priority=event.get("priority", 50),
            voice_limit=event.get("limit", 4), cooldown_seconds=event.get("cooldown", 0), loop=event.get("loop", False),
            selection="Shuffle bag without immediate repeat when >1 variant. Loops use pitch1; local body path may override gain. 56 world and20 flat voices shared.")
        entry["files"] = []
        for res in event.get("files", []) + [p for layer in event.get("layers", []) for p in layer["files"]]:
            rel = res.removeprefix("res://assets/audio/")
            files.add(rel)
            path = ROOT / "game/assets/audio" / rel
            stored = levels.get(rel, {})
            current_hash = digest(path) if path.exists() else None
            entry["files"].append(dict(path=relative(path), exists=path.exists(), sha256=current_hash,
                manifest_hash_matches=current_hash == stored.get("sha256") if current_hash else False,
                stored_measurements=stored, recipe=recipes.get(rel),
                recipe_matches_measurements=(recipes[rel]["recipe_hash"] == stored.get("recipe")) if rel in recipes else None))
        entry["listening_evidence"] = ([GRASS_GAMEPLAY_FEEDBACK] if key.startswith("footstep.") and key.endswith(".grass") else [])
        entry["visual_timing_evidence"] = []
        entry["technical_gameplay_evidence"] = [capture["trace"] for capture in gameplay
            if any(event["event"] == key for event in capture["event_counts"])]
        entry["user_accepted"] = False
        entry["user_accepted_scope"] = ("Boink absence only in retained gameplay-grass-mix.wav; full event quality remains open."
            if entry["listening_evidence"] else None)
        entries.append(entry)

    snapshots = [CATALOGUE, LEVELS, AUDIO / "events.py", AUDIO / "repair_actions.py", AUDIO / "sources.py", AUDIO / "animal_voices.py"]
    for entry in entries:
        for ref in entry["runtime_evidence"]:
            file = ROOT / ref.split(":", 1)[0]
            if file.exists():
                snapshots.append(file)
    checked_files = [f for e in entries for f in e["files"]]
    scenarios = [dict(priority=name, work=text, status="STILL_OPEN", recordings=[], reviewer=None) for name, text in SCENARIOS]
    for scenario in scenarios[:3]:
        scenario["recordings"] = [capture["trace"] for capture in gameplay]
        scenario["status"] = "GRASS_PARTIALLY_TECHNICALLY_CAPTURED; RETAINED_MIX_BOINK_ABSENCE_ACCEPTED; OTHER_MATERIALS_AND_UNEXERCISED_STATES_OPEN"
    data = dict(schema=2, scope="Full current event catalogue; static trigger review, file-integrity snapshot, retained grass gameplay technical evidence and scoped owner boink-absence acceptance. No whole-catalogue or visual quality acceptance.",
        events=len(entries), unique_files=len(files), recipe_segment_scope=recipe_scope,
        technical_checks=dict(unique_event_keys=len({e["event"] for e in entries}) == len(entries),
            missing_files=sum(not f["exists"] for f in checked_files),
            file_manifest_mismatches=sum(not f["manifest_hash_matches"] for f in checked_files),
            recipe_manifest_mismatches=sum(f["recipe_matches_measurements"] is False for f in checked_files),
            recipe_files_exported=sum(f["recipe"] is not None for f in checked_files),
            fresh_loudness_measurement=False, audio_playback=False, gameplay_recording=False,
            activity_boundary="These three flags describe this generator invocation, not the separately captured gameplay evidence.",
            retained_gameplay_captures=len(gameplay)),
        provenance_boundary="When recipes are omitted, recipe_section is a navigation pointer, not per-file source verification. Generated intermediates require builder inspection for inner recordings.",
        input_sha256={relative(path): digest(path) for path in sorted(set(snapshots))},
        owner_feedback=[OWNER_FEEDBACK, SOURCE_SAMPLE_FEEDBACK,
            dict(**GRASS_GAMEPLAY_FEEDBACK, sha256=digest(ROOT / GRASS_GAMEPLAY_FEEDBACK["path"]))],
        current_footstep_integration=FOOTSTEP_INTEGRATION,
        technical_gameplay_evidence=gameplay,
        local_source_candidates=candidate_sources(recipes),
        acceptance_scenarios=scenarios,
        entries=entries)
    REPORT.parent.mkdir(parents=True, exist_ok=True)
    REPORT.with_suffix(".json").write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    provenance_note = (f"Exact source segments are exported for {data['technical_checks']['recipe_files_exported']} files ({recipe_scope} scope). "
                       if recipes else "No exact source segments were exported by this invocation. ")
    if recipe_scope != "all":
        provenance_note += "Other events carry recipe-section provenance pointers, not a completed per-file source reconstruction. Full recipe enumeration is deliberately deferred because unrelated synthesis is expensive. "
    lines = ["# Audio action checklist — 16 October 2026", "",
        f"Snapshot of **{len(entries)} events / {len(files)} unique files**. The overview below groups the actions; the JSON has every event and variant, source segment (when exported), hash, stored measurement, actual inspected runtime path and candidate item-role consumer.", "",
        "**Technical inspection is not listening.** This generator establishes catalogue coverage, recorded routing evidence, current file/manifest agreement and links to existing gameplay captures. It does not play audio, measure audible latency, assess timbre/masking or grant visual, audible or user acceptance. The owner's reported rejection and later narrow source-sample and grass-gameplay boink-absence results are recorded explicitly below; no agent listening or visual-timing review is claimed.", "",
        "Reproduce: `python -I tools/audio/audit_catalogue.py --footstep-recipes`. " + provenance_note + "Recipe evaluation can populate the ignored synthesis cache and run onset analysis; it does not encode game assets. Re-run after source or routing changes. JSON input hashes identify the exact inspected snapshot.", "",
        "**Owner listening result: initial footstep repair rejected.** The owner listened to the first A/B reel and reports the boink remains throughout, mainly while walking and jumping. Removing generic heavy-impact thumps was insufficient. Base walking recordings were unchanged; their material identification, recorded contacts and processing require further diagnosis. The first repair must not be represented as accepted or as resolving the complaint.", "",
        "**Later sample result: boink absent in both source sets.** For the six-second `source-candidates-v2/new-ground-contacts.wav` comparison the owner said, \"Neither set has it\". This result applies only to the four exact processed BSB1117 dry contacts and four exact processed BSB2889 organic contacts in that file. It does not accept the full source recordings, newly selected variants, modified gait processing, gameplay timing or the game's wider mix. The JSON preserves the audition file hash and exact selection/filter settings. The separate grass gameplay result follows.", "",
        "**Grass gameplay: owner confirms the boink is absent.** The owner said, \"okay gameplay-grass-mix.wav doesnt have it\". This accepts absence of the reported boink in that exact retained grass gameplay mix, including its captured walking, running, crouching and landings. It does not accept the second verification recording, dirt gameplay, other materials, transitions, broader mix quality or the full catalogue.", "",
        "Two actual hosted-client movement captures separately passed technical checks. The retained first run contains walk (11.25 m), run (21.81 m), crouch (5.2 m), three jumps/landings and silent idle. It logged nine walk, nine run and seven crouch contacts, one final walk contact during stop-run, and one landing per jump. Its Master WAV is 26.848 s and Footsteps WAV 26.933 s, with no clipped PCM reported by the coordinated recording check. The hardened repeat produced two valid, nonzero 26.848 s WAVs, each 0.010 s from the harness duration. These checks establish captured output and exercised grass actions, not realism or perceived timing. No owner audition of the repeat is claimed. Dirt gameplay recording remains open.", "",
        "Retained evidence: [grass game mix](../audio/repairs/2026-10-16/gameplay-grass-mix.wav), [isolated grass Footsteps bus](../audio/repairs/2026-10-16/gameplay-grass-feet.wav), [grass trace](../audio/repairs/2026-10-16/gameplay-grass.json), [owner-auditioned source contacts](../audio/repairs/2026-10-16/approved-source-contacts.wav). The JSON separately summarizes the hardened repeat under `.artifacts/audio-first/gameplay-final`. Trace timestamps are sound-start requests, not sample-accurate audible onsets. The final engine catalogue check reported 235 events / 775 files and zero problems; engine loading/measurement is technical evidence only.", "",
        "**Current integration:** grass uses the four selected BSB2889 organic contacts and dirt uses the four selected BSB1117 dry contacts. Walk/run share those contacts with gait cadence/level changes; crouch adds a 12 ms fade; landing plays one of the first three single contacts. These eight events use pitch 0.98–1.02 and volume jitter ±0.8 dB. Dirt additionally uses a gentle compressor (threshold −15 dB, ratio 1.5, attack 0.2 ms, release 25 ms) added after the source audition. That brought the sharp third contact within the unchanged loudness/peak bounds; dirt has not received user gameplay acceptance. The final file/provenance snapshot below reflects the integrated assets; only the retained grass mix has scoped runtime boink-absence acceptance.", "",
        "The duplicate bow-release path is removed, dry fire now has a per-press consumer, and melee contact uses the confirmed attack's weapon. These are implementation findings, not audible acceptance. Shell playback and successful-repair audio still have no inspected gameplay consumer.", "",
        "Natural gravel maps to dirt; there is no separate gravel event or jump-takeoff event. Exact prop/monument materials, live weather coupling, field-recorded animal voices, and complete gun/impact mixing remain open. A catalogue event may exist without an active gameplay trigger, and a gameplay action may have no catalogue event.", "",
        "Playback uses shuffled variants and bounded pitch/jitter. Local feet are flat with -3 dB; remote feet are positional. 3D events use inverse-distance attenuation and occlusion, with distance layers/propagation only where configured. The report retains per-event numeric settings; nominal loudness and bytes do not establish realism.", "",
        "## Required gameplay review", "", "| Priority | Scenario | Evidence |", "|---|---|---|"]
    for scenario in scenarios:
        evidence = ("Owner confirms boink absent in retained grass mix. Broader quality, dirt, other materials and unexercised states remain open."
                    if scenario["recordings"] else "STILL OPEN — no recording/listener acceptance attached")
        lines.append(f"| {scenario['priority']} | {scenario['work']} | {evidence} |")
    lines += ["", "## Available contact recordings to compare", "",
        "Metadata and source hashes only: **none of these alternatives was listened to by this audit**. Selection below reflects the recording's stated material, not a claim that its timbre is better. All are already local and have repository provenance; no paid asset or download was used. The JSON retains exact filenames and source/archive hashes. Mud, leaves and gravel must keep their material identity rather than becoming an indiscriminate grass replacement.", "",
        "| Source | Recorded description / clip | Duration | Disposition |", "|---|---|---|---|"]
    for source in data["local_source_candidates"]:
        lines.append(f"| {source['source']} | {source['title']} | {source['duration_seconds']:.2f}s | " +
                     ("Used by current footsteps; source subset and scoped grass gameplay feedback above; dirt runtime open" if source["already_used_in_current_human_steps"] else
                      "Current use unverified without recipe export" if source["already_used_in_current_human_steps"] is None else
                      "Prior baseline, replaced for grass/dirt" if source.get("used_before_current_base_contact_replacement") else
                      "Candidate for audition; quality unassessed") + " |")
    lines += ["", "Other permitted local packs: Fantozzi's six sand contacts (0.30–0.44 s) and six stone contacts (0.32–0.37 s) are already used. Corsica_S has 42 snow-covered-gravel/ice contacts (0.44–0.72 s); their stated snowy material does not make them dry-ground recordings. Kenney Impact Sounds contains five `Audio/footstep_grass_000–004.ogg` clips (0.59–0.78 s), available for audition with no realism claim. All three source archive hashes were checked against `art/audio/sources.py` during this inventory. The local `Assets/Audio` folder remains empty; available packs are under `Assets/Downloads`."]
    lines += ["", "## Whole catalogue coverage", "", "[The machine-readable checklist](audio-action-checklist.json) contains one complete action row per event and one manifest row per variant. It retains the exact event, actual inspected trigger/timing, source segments, spatial and distance settings, variation, mix, observed defect, replacement disposition and scoped owner listening evidence for the four captured grass events. Item-role candidates are clearly separate from inspected trigger evidence. **Retain pending audition** is a review disposition, not approval of the current sound.", "",
              "| Family | Events / variant references | Runtime route | Review focus |", "|---|---|---|---|"]
    def safe(s):
        return str(s).replace("|", "/").replace("\n", " ")
    groups = {}
    for entry in entries:
        group = entry["event"].split(".")[0]
        groups.setdefault(group, []).append(entry)
    for group, members in groups.items():
        first = members[0]
        lines.append("| " + " | ".join(safe(x) for x in [group,
            f"{len(members)} / {sum(len(e['files']) for e in members)}",
            first["trigger"], first["observed_defect_or_risk"]]) + " |")
    lines += ["", "## Catalogue rows without an active consumer", "",
        "These findings came from inspecting the actual routes, item mappings and battery tables, not merely a literal search (many working events are named dynamically). They remain open dispositions; dormant assets do not by themselves prove the underlying gameplay action is missing.", "",
        "| Event | Finding |", "|---|---|"]
    lines += [f"| `{entry['event']}` | {safe(entry['observed_defect_or_risk'])} |" for entry in entries
              if entry["status"].startswith(("ROUTE_GAP", "CATALOGUE_ONLY"))]
    lines += ["", "## Evidence labels", "",
        "- **Implemented route inspected:** named code/data path exists; this does not prove that a complete gameplay sequence reaches it correctly.",
        "- **File integrity checked:** each current audio file is present and its bytes match its stored measurement manifest. Measurements are inherited build outputs, not newly measured by this audit.",
        "- **Gameplay technically captured:** existing grass Master/Footsteps recordings, event traces and harness checks are retained and summarized. This audit did not capture or listen to them. Dirt and actual combat playback have no acceptance claim.",
        "- **Visually/audibly reviewed:** the owner's exact source-sample and retained grass-mix boink-absence observations are attached. No agent audition, visual-timing review or owner audition of the second verification recording is claimed.",
        "- **User accepted:** full-event quality remains false for every row. Four grass event rows carry separate scoped acceptance: the reported boink is absent in the retained gameplay-grass-mix.wav. This does not grant whole-audio quality acceptance.",
        "- **Still open:** artistic mix/realism, material transitions, competition, action synchronization and every unrecorded scenario above."]
    REPORT.with_suffix(".md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    bad = sum(not f["manifest_hash_matches"] for e in entries for f in e["files"])
    print(f"Wrote {relative(REPORT)}.json/.md: {len(entries)} events, {len(files)} unique files, {bad} file/manifest mismatches; no listening performed.")


if __name__ == "__main__":
    main()
