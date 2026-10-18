"""Package a two-clip comparison without changing Blender or candidate sources.

python art/cascadeur_comparison/package_compare.py

Candidate inputs: <out>/candidates/fps/{hatchet_chop,torch_equip}.mp4
Optional stills use the same name with .png. Optional candidates/metadata.json:
{"clips": {"fps": {"hatchet_chop": {"rendering_method": "Native Cascadeur
viewport frames", "notes": "..."}}}}

All generated files, including media copies, stay inside <out>/review/.
Re-running discovers candidate files; no source media are edited or removed.
"""

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil


ROOT = Path(__file__).resolve().parents[2]
NAMES = ("hatchet_chop", "torch_equip")
KINDS = ("fps",)
CAPTURE_METHOD = "Native Cascadeur viewport frames · FPS"
COMPARISON_CONDITIONS = (
    "Camera pose, 59.822° field of view and 896 × 630 framing match the v3 FPS camera. "
    "Materials and lighting differ between the Blender render and Cascadeur viewport. "
    "Compare motion, grip and silhouette; surface appearance is not a matched comparison."
)


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def copy_media(source, destination, review):
    if not source.is_file() or source.stat().st_size == 0:
        return None
    sha = digest(source)
    destination.parent.mkdir(parents=True, exist_ok=True)
    if not destination.is_file() or digest(destination) != sha:
        shutil.copy2(source, destination)
    if digest(destination) != sha:
        raise ValueError(f"Copy verification failed: {destination}")
    relative = destination.relative_to(review).as_posix()
    return {"path": relative, "url": f"{relative}?v={sha[:20]}",
            "sha256": sha, "bytes": destination.stat().st_size,
            "source": str(source)}


HTML = r'''<!doctype html>
<html lang="en">
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Blender v3 / Cascadeur · Motion comparison</title>
<style>
:root{color-scheme:dark;font:15px/1.5 system-ui,sans-serif;background:#10171b;color:#eaf0ef}
*{box-sizing:border-box}[hidden]{display:none!important}body{margin:0}main{max-width:1530px;margin:auto;padding:30px 28px 44px}
h1{font-size:30px;letter-spacing:-.7px;margin:0 0 8px}h2{font-size:17px;margin:0}.eyebrow{font-size:11px;letter-spacing:1.8px;color:#94b8b3;text-transform:uppercase;margin-bottom:10px}
p{margin:7px 0}.intro{max-width:980px;color:#afbec5}.choices{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:25px 0 18px}.clip-buttons{display:flex;gap:8px}.view-choice{margin-left:auto}
button,select{font:inherit;background:#253239;color:inherit;border:1px solid #4c5e67;border-radius:7px;padding:9px 14px;cursor:pointer}button:hover{background:#34464d}button[aria-pressed=true],.primary{background:#a9d9ca;color:#142a23;border-color:#a9d9ca}.primary:hover{background:#c4ebdf}button:disabled{opacity:.4;cursor:not-allowed}
button:focus-visible,input:focus-visible,select:focus-visible,a:focus-visible{outline:3px solid #b6eadb;outline-offset:3px}label{display:flex;gap:8px;align-items:center}
.players{display:grid;grid-template-columns:1fr 1fr;gap:18px}.panel{background:#1a252b;border:1px solid #39474e;border-radius:12px;overflow:hidden}.panel header{padding:15px 17px;display:flex;justify-content:space-between;align-items:center;gap:10px}.tag{border:1px solid #50626b;color:#b5c8cf;border-radius:20px;padding:3px 9px;font-size:11px}
.media{position:relative;aspect-ratio:896/630;background:#0c1216}.media video,.media img{display:block;width:100%;height:100%;object-fit:contain}.empty{height:100%;display:grid;place-content:center;text-align:center;padding:30px;color:#aabdc5}.empty strong{color:#e8eeed;font-size:18px}.empty p{max-width:330px;font-size:13px}.panel footer{padding:11px 17px;min-height:80px;font-size:12px;color:#aebfc7}.panel footer p{margin:2px 0}.links{display:flex;gap:15px;min-height:20px}a{color:#a2ddca;text-underline-offset:3px}
.transport{display:flex;align-items:center;gap:9px;flex-wrap:wrap;margin-top:19px}.repeat{margin-left:auto;font-size:13px}.timeline{display:flex;gap:13px;align-items:center;margin-top:16px}.timeline input{flex:1;accent-color:#a9d9ca;min-width:80px}.time{font-variant-numeric:tabular-nums;font-size:13px;color:#b8cbd2;min-width:110px;text-align:right}.status{font-size:13px;color:#ebc491;min-height:23px;margin-top:10px}.notes{border-top:1px solid #39474e;margin-top:19px;padding-top:17px;display:grid;grid-template-columns:1fr 1fr;gap:20px}.notes p{color:#aabcc4;font-size:13px}.note{color:#91a8b1;font-size:12px;margin-top:23px;max-width:1100px}
@media(max-width:700px){main{padding:23px 14px}.players,.notes{grid-template-columns:1fr}.view-choice,.repeat{margin-left:0}h1{font-size:25px}.panel footer{min-height:65px}}
</style>
<main>
 <div class="eyebrow">Hollow Universe · isolated animation comparison</div>
 <h1>Blender v3 / Cascadeur</h1>
 <p class="intro">Compare the first-person wrist, grip, wind-up, reach and recovery. The left panel is a verified copy of the preserved Blender v3 video. The right panel shows native Cascadeur viewport frames when the candidate video is available.</p>
 <div class="choices">
  <div class="clip-buttons" role="group" aria-label="Animation"><button id="hatchet_chop" aria-pressed="true">Hatchet chop</button><button id="torch_equip" aria-pressed="false">Torch equip</button></div>
  <span class="view-choice tag">First-person test · FPS only</span>
 </div>
 <div class="players">
  <section class="panel"><header><h2>Blender v3</h2><span class="tag">Preserved baseline</span></header>
   <div class="media"><video id="baseline" playsinline preload="metadata" aria-label="Blender v3 animation"></video><img id="baseline-image" alt="Blender v3 still" hidden><div id="baseline-empty" class="empty" hidden></div></div>
   <footer><p id="baseline-info"></p><div class="links"><a id="baseline-still" target="_blank" rel="noopener" hidden>Open still</a><a id="baseline-video" target="_blank" rel="noopener" hidden>Open video</a></div></footer>
  </section>
  <section class="panel"><header><h2>Cascadeur</h2><span class="tag" id="candidate-tag">Awaiting candidate</span></header>
   <div class="media"><video id="candidate" playsinline preload="metadata" aria-label="Cascadeur candidate animation" hidden></video><img id="candidate-image" alt="Cascadeur candidate still" hidden><div id="candidate-empty" class="empty"><strong>Awaiting Cascadeur candidate</strong><p>No candidate video is present for this clip and view. The Blender baseline remains available.</p></div></div>
   <footer><p id="candidate-info"></p><div class="links"><a id="candidate-still" target="_blank" rel="noopener" hidden>Open still</a><a id="candidate-video" target="_blank" rel="noopener" hidden>Open video</a></div></footer>
  </section>
 </div>
 <div class="transport" aria-label="Synchronized playback controls">
  <button id="play" class="primary">Play baseline</button><button id="restart">Restart</button><button id="beat">Jump to contact</button>
  <label>Speed <select id="speed" aria-label="Playback speed"><option value="0.25">0.25×</option><option value="0.5">0.5×</option><option value="1" selected>1×</option></select></label>
  <label class="repeat"><input type="checkbox" id="repeat" checked>Repeat</label>
 </div>
 <div class="timeline"><label for="seek">Time</label><input id="seek" type="range" min="0" max="1" step="0.001" value="0" aria-label="Animation time"><output id="time" class="time">0.000 / 0.000 s</output></div>
 <p id="status" class="status" role="status" aria-live="polite"></p>
 <div class="notes"><div><h2>What to inspect</h2><p id="focus"></p></div><div><h2>Comparison conditions</h2><p id="conditions"></p></div></div>
 <p class="note">This comparison covers the two first-person animations. Candidate presence is not visual acceptance. Native Cascadeur viewport lighting and materials differ from the Blender renders. Both panels use the same elapsed time without stretching either clip; if their durations differ, the shorter clip holds its last frame. No game assets or original Blender files are changed.</p>
 <p class="note"><a href="manifest.json" target="_blank" rel="noopener">Media manifest and SHA-256 hashes</a></p>
</main>
<script>
const DATA=__DATA__;
const $=id=>document.getElementById(id), keys=['baseline','candidate'];
const videos=keys.map($), failures=new Map();
let clip='hatchet_chop',view='fps',generation=0,request=0,intent=false,clockStart=0,offset=0,dragging=false,rate=1;
const record=()=>DATA.clips[clip][view];
const selected=()=>videos.filter(v=>record()[v.id].video);
const duration=()=>Math.max(0,...selected().map(v=>Number.isFinite(v.duration)?v.duration:0));
const ready=()=>selected().every(v=>v.readyState>=2&&!failures.has(v.id));
const currentTime=()=>intent?Math.min(duration(),offset+(performance.now()-clockStart)/1000*rate):offset;
const sec=n=>Number(n).toFixed(3);
function setLink(id,media){$(id).hidden=!media;if(media)$(id).href=media.url;else $(id).removeAttribute('href')}
function renderStatus(){
 const errors=selected().filter(v=>failures.has(v.id));
 const loading=selected().filter(v=>v.readyState<2&&!failures.has(v.id));
 let message='';
 if(errors.length)message=errors.map(v=>(v.id==='baseline'?'Blender v3':'Cascadeur')+': '+failures.get(v.id)).join(' · ');
 else if(loading.length)message='Loading '+loading.map(v=>v.id==='baseline'?'Blender v3':'Cascadeur').join(' and ')+' video…';
 else if(!record().candidate.video)message=record().candidate.still?'Candidate still available; waiting for candidate animation video.':'Awaiting Cascadeur candidate. Play the preserved Blender baseline while the test is prepared.';
 else message='Both videos loaded. Candidate quality has not been accepted.';
 $('status').textContent=message;
 const enabled=selected().length>0&&ready();
 $('play').disabled=!enabled;$('restart').disabled=!enabled;$('seek').disabled=!enabled;$('beat').disabled=!enabled;
 $('play').textContent=(intent?'Pause':'Play')+(record().candidate.video?' both':' baseline');
 $('seek').max=String(duration()||1);
}
function seekMedia(time){selected().forEach(v=>{if(v.readyState){const end=Math.max(0,v.duration-0.001),target=Math.min(time,end);if(Math.abs(v.currentTime-target)>.002)v.currentTime=target}})}
function pause(){offset=currentTime();intent=false;++request;videos.forEach(v=>v.pause());renderStatus()}
async function play(){
 if(!ready())return;
 if(offset>=duration()-.002)offset=0;
 seekMedia(offset);intent=true;clockStart=performance.now();
 const token=++request,revision=generation;
 const results=await Promise.allSettled(selected().filter(v=>offset<v.duration-.002).map(v=>v.play()));
 if(token!==request||revision!==generation||!intent)return;
 const failure=results.find(r=>r.status==='rejected');
 if(failure){pause();$('status').textContent='Playback could not start: '+failure.reason.message;return}
 renderStatus();
}
function setTime(time){const wasPlaying=intent;pause();offset=Math.max(0,Math.min(Number(time)||0,duration()));seekMedia(offset);if(wasPlaying&&offset<duration())play()}
function load(){
 pause();++generation;offset=0;failures.clear();
 for(const name of ['hatchet_chop','torch_equip'])$(name).setAttribute('aria-pressed',String(name===clip));
 keys.forEach(key=>{
  const media=record()[key],video=$(key),image=$(key+'-image'),empty=$(key+'-empty');
  video.hidden=!media.video;image.hidden=Boolean(media.video)||!media.still;empty.hidden=Boolean(media.video||media.still);
  video.removeAttribute('src');video.removeAttribute('poster');
  if(media.still){image.src=media.still.url;video.poster=media.still.url}else image.removeAttribute('src');
  if(media.video)video.src=media.video.url;
  video.load();video.playbackRate=Number($('speed').value);
  setLink(key+'-still',media.still);setLink(key+'-video',media.video);
 });
 $('candidate-tag').textContent=record().candidate.video?'Unaccepted candidate':record().candidate.still?'Still only':'Awaiting candidate';
 $('baseline-info').textContent='Blender render · '+view.toUpperCase()+' · verified copy of v3';
 $('candidate-info').textContent=record().candidate.video||record().candidate.still?record().candidate.rendering_method:'Candidate media has not been generated for this view.';
 $('focus').textContent=clip==='hatchet_chop'?'Check wrist alignment at ready, wind-up and impact; blade direction, grip contact, elbow motion, forward extension and recovery.':'Check wrist alignment during the raise and settled hold; fingers around the handle, thumb contact, elbow placement and final tool angle.';
 $('beat').textContent=clip==='hatchet_chop'?'Jump to contact':'Jump to hold';
 $('conditions').textContent=record().candidate.notes||'A fair comparison needs the same character, props, camera and timing. Read candidate capture details before judging a rendering difference as a pose difference.';
 renderStatus();
}
videos.forEach(v=>{
 for(const event of ['loadedmetadata','loadeddata','canplay','durationchange'])v.addEventListener(event,renderStatus);
 v.addEventListener('error',()=>{if(!record()[v.id].video)return;failures.set(v.id,v.error?.message||'The browser could not decode this video.');pause();renderStatus()});
 v.addEventListener('waiting',()=>{if(intent)$('status').textContent='Buffering '+(v.id==='baseline'?'Blender v3':'Cascadeur')+'…'});
});
for(const name of ['hatchet_chop','torch_equip'])$(name).onclick=()=>{clip=name;load()};
$('play').onclick=()=>intent?pause():play();
$('restart').onclick=()=>{pause();offset=0;seekMedia(0);play()};
$('beat').onclick=()=>{pause();offset=DATA.clips[clip].beat;seekMedia(offset)};
$('speed').onchange=()=>{const wasPlaying=intent;pause();rate=Number($('speed').value);videos.forEach(v=>v.playbackRate=rate);if(wasPlaying)play()};
$('seek').addEventListener('input',()=>{dragging=true;setTime($('seek').value)});
$('seek').addEventListener('change',()=>{dragging=false});
function tick(){
 let time=currentTime();
 if(intent&&time>=duration()){
  pause();if($('repeat').checked){offset=0;seekMedia(0);play()}else{offset=duration();seekMedia(offset)}
  time=offset;
 }else if(intent){
  selected().forEach(v=>{const target=Math.min(time,Math.max(0,v.duration-.001));if(Math.abs(v.currentTime-target)>.075)v.currentTime=target});
 }
 if(!dragging)$('seek').value=String(time);
 $('time').textContent=sec(time)+' / '+sec(duration())+' s';
 requestAnimationFrame(tick);
}
load();requestAnimationFrame(tick);
</script>
</html>
'''


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, default=ROOT / ".artifacts/character-trial-v3")
    parser.add_argument("--out", type=Path, default=ROOT / ".artifacts/character-trial-cascadeur-v3-comparison")
    args = parser.parse_args(argv)
    baseline, out = args.baseline.resolve(), args.out.resolve()
    review = out / "review"
    if review == baseline or baseline in review.parents or review in baseline.parents:
        raise ValueError("Comparison review must be separate from the preserved baseline")
    candidate = out / "candidates"
    metadata_path = candidate / "metadata.json"
    metadata = json.loads(metadata_path.read_text(encoding="utf-8")) if metadata_path.is_file() else {}
    data = {"generated_utc": datetime.now(timezone.utc).isoformat(),
            "baseline": str(baseline), "candidate_input": str(candidate), "clips": {}}
    for name in NAMES:
        data["clips"][name] = {"beat": 0.6 if name == "hatchet_chop" else 1.1}
        for kind in KINDS:
            details = metadata.get("clips", {}).get(kind, {}).get(name, {})
            entry = {}
            for label in ("baseline", "candidate"):
                source_video = baseline / "videos" / kind / f"{name}.mp4" if label == "baseline" else candidate / kind / f"{name}.mp4"
                source_still = baseline / "review" / kind / f"{name}.png" if label == "baseline" else candidate / kind / f"{name}.png"
                target = review / "media" / label / kind
                entry[label] = {"video": copy_media(source_video, target / f"{name}.mp4", review),
                                "still": copy_media(source_still, target / f"{name}.png", review)}
                if label == "baseline" and not entry[label]["video"]:
                    raise FileNotFoundError(f"Preserved baseline video missing: {source_video}")
            entry["candidate"].update({"rendering_method": str(details.get("rendering_method", CAPTURE_METHOD)),
                                       "notes": str(details.get("notes", COMPARISON_CONDITIONS)), "accepted": False})
            data["clips"][name][kind] = entry
    review.mkdir(parents=True, exist_ok=True)
    (review / "manifest.json").write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    payload = json.dumps(data, ensure_ascii=True).replace("<", "\\u003c")
    (review / "index.html").write_text(HTML.replace("__DATA__", payload), encoding="utf-8")
    count = sum(bool(data["clips"][name][kind]["candidate"]["video"]) for name in NAMES for kind in KINDS)
    print(json.dumps({"review": str(review / "index.html"), "candidate_videos": count, "expected_candidate_videos": 2}))


if __name__ == "__main__":
    main()
