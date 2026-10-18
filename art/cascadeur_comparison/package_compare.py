"""Package FPS weapon motion comparisons without changing source media.

python art/cascadeur_comparison/package_compare.py

Candidate inputs: <out>/candidates/{fps,tps}/<clip>.mp4
FPS is the default; use --kinds fps tps to expose both views.
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
NAMES = ("hatchet_chop", "torch_equip", "heavyaxe_chop", "pickaxe_swing", "pistol_fire", "pistol_reload", "rifle_fire", "rifle_reload")
KINDS = ("fps",)
TITLES = {"hatchet_chop": "Hatchet chop", "torch_equip": "Torch equip", "heavyaxe_chop": "Heavy axe chop", "pickaxe_swing": "Pickaxe swing",
          "pistol_fire": "Pistol fire", "pistol_reload": "Pistol reload", "rifle_fire": "Rifle fire", "rifle_reload": "Rifle reload"}
FOCUS = {
    "hatchet_chop": "Check wrist alignment at ready, wind-up and impact; blade direction, grip contact, elbow motion, forward extension and recovery.",
    "torch_equip": "Check wrist alignment during the raise and settled hold; fingers around the handle, thumb contact, elbow placement and final tool angle.",
    "heavyaxe_chop": "Check the weight of the wind-up, wrist alignment, grip contact, chopping reach and recovery.",
    "pickaxe_swing": "Check the grip, point trajectory, wrist alignment, forward reach and recovery.",
    "pistol_fire": "Check trigger-hand alignment, supporting hand contact, recoil direction and return to aim.",
    "pistol_reload": "Check magazine handling, hand contact, wrist shape, timing and return to aim.",
    "rifle_fire": "Check both grips, shoulder support, recoil direction and return to aim.",
    "rifle_reload": "Check supporting-hand transitions, magazine handling, wrist shape and return to aim.",
}
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
p{margin:7px 0}.intro{max-width:980px;color:#afbec5}.choices{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:25px 0 18px}.clip-buttons{display:flex;gap:8px;flex-wrap:wrap}.clip-buttons button small{display:block;font-size:10px;opacity:.7}.view-choice{margin-left:auto}
button,select{font:inherit;background:#253239;color:inherit;border:1px solid #4c5e67;border-radius:7px;padding:9px 14px;cursor:pointer}button:hover{background:#34464d}button[aria-pressed=true],.primary{background:#a9d9ca;color:#142a23;border-color:#a9d9ca}.primary:hover{background:#c4ebdf}button:disabled{opacity:.4;cursor:not-allowed}
button:focus-visible,input:focus-visible,select:focus-visible,a:focus-visible{outline:3px solid #b6eadb;outline-offset:3px}label{display:flex;gap:8px;align-items:center}
.players{display:grid;grid-template-columns:1fr 1fr;gap:18px}.players.candidate-only{grid-template-columns:1fr}.baseline-notice{color:#b8cbd2;font-size:13px;margin:0 0 12px}.panel{background:#1a252b;border:1px solid #39474e;border-radius:12px;overflow:hidden}.panel header{padding:15px 17px;display:flex;justify-content:space-between;align-items:center;gap:10px}.tag{border:1px solid #50626b;color:#b5c8cf;border-radius:20px;padding:3px 9px;font-size:11px}
.media{position:relative;aspect-ratio:896/630;background:#0c1216}.media video,.media img{display:block;width:100%;height:100%;object-fit:contain}.empty{height:100%;display:grid;place-content:center;text-align:center;padding:30px;color:#aabdc5}.empty strong{color:#e8eeed;font-size:18px}.empty p{max-width:330px;font-size:13px}.panel footer{padding:11px 17px;min-height:80px;font-size:12px;color:#aebfc7}.panel footer p{margin:2px 0}.links{display:flex;gap:15px;min-height:20px}a{color:#a2ddca;text-underline-offset:3px}
.transport{display:flex;align-items:center;gap:9px;flex-wrap:wrap;margin-top:19px}.repeat{margin-left:auto;font-size:13px}.timeline{display:flex;gap:13px;align-items:center;margin-top:16px}.timeline input{flex:1;accent-color:#a9d9ca;min-width:80px}.time{font-variant-numeric:tabular-nums;font-size:13px;color:#b8cbd2;min-width:110px;text-align:right}.status{font-size:13px;color:#ebc491;min-height:23px;margin-top:10px}.notes{border-top:1px solid #39474e;margin-top:19px;padding-top:17px;display:grid;grid-template-columns:1fr 1fr;gap:20px}.notes p{color:#aabcc4;font-size:13px}.note{color:#91a8b1;font-size:12px;margin-top:23px;max-width:1100px}
@media(max-width:700px){main{padding:23px 14px}.players,.notes{grid-template-columns:1fr}.view-choice,.repeat{margin-left:0}h1{font-size:25px}.panel footer{min-height:65px}}
</style>
<main>
 <div class="eyebrow">Hollow Universe · isolated animation comparison</div>
 <h1>Blender v3 / Cascadeur</h1>
 <p class="intro">Inspect first-person tools and weapons: wrist shape, grip, movement and recovery. Hatchet chop and torch equip can be compared against preserved Blender v3 videos. New weapon clips have no v3 baseline and are labelled accordingly.</p>
 <div class="choices">
  <div class="clip-buttons" role="group" aria-label="Animation">__CLIP_BUTTONS__</div>
  __VIEW_CONTROLS__
 </div>
 <p id="baseline-notice" class="baseline-notice" hidden>Candidate only: no Blender v3 baseline exists for this clip. Visual quality has not been accepted.</p>
 <div id="players" class="players">
  <section id="baseline-panel" class="panel"><header><h2>Blender v3</h2><span class="tag" id="baseline-tag">Preserved baseline</span></header>
   <div class="media"><video id="baseline" playsinline preload="auto" aria-label="Blender v3 animation"></video><img id="baseline-image" alt="Blender v3 still" hidden><div id="baseline-empty" class="empty" hidden><strong>No v3 baseline for this clip</strong><p>This weapon animation is a new Cascadeur candidate. No earlier Blender comparison is available.</p></div></div>
   <footer><p id="baseline-info"></p><div class="links"><a id="baseline-still" target="_blank" rel="noopener" hidden>Open still</a><a id="baseline-video" target="_blank" rel="noopener" hidden>Open video</a></div></footer>
  </section>
  <section class="panel"><header><h2>Cascadeur</h2><span class="tag" id="candidate-tag">Awaiting candidate</span></header>
   <div class="media"><video id="candidate" playsinline preload="auto" aria-label="Cascadeur candidate animation" hidden></video><img id="candidate-image" alt="Cascadeur candidate still" hidden><div id="candidate-empty" class="empty"><strong>Awaiting Cascadeur candidate</strong><p>No candidate video is present for this clip and view yet.</p></div></div>
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
 <p class="note">Candidate presence is not visual acceptance. Native Cascadeur viewport lighting and materials differ from the Blender renders. Both panels use the same elapsed time without stretching either clip; if their durations differ, the shorter clip holds its last frame. No game assets or original Blender files are changed.</p>
 <p class="note"><a href="manifest.json" target="_blank" rel="noopener">Media manifest and SHA-256 hashes</a></p>
</main>
<script>
const DATA=__DATA__;
const $=id=>document.getElementById(id), keys=['baseline','candidate'];
const videos=keys.map($), failures=new Map();
// BEGIN TRANSPORT — also exercised by test_transport.cjs without a browser.
function createTransport(allMedia, hooks={}){
 let media=[],epoch=0,intent=false,running=false,offset=0,rate=1,state='paused',message='';
 const waiting=new Set(),notify=()=>hooks.change?.();
 const duration=()=>Math.max(0,...media.map(v=>Number.isFinite(v.duration)?v.duration:0));
 const leader=()=>media.reduce((best,v)=>!best||v.duration>best.duration?v:best,null);
 const time=()=>running?Math.min(duration(),leader()?.currentTime||0):offset;
 function cancel(){++epoch;for(const abort of [...waiting])abort();waiting.clear()}
 function pauseMedia(){media.forEach(v=>v.pause())}
 function pause(){offset=time();cancel();intent=false;running=false;pauseMedia();state='paused';notify()}
 function fail(error){cancel();intent=false;running=false;pauseMedia();state='error';message=error?.message||String(error);notify()}
 function waitFor(video,predicate,token){
  return new Promise((resolve,reject)=>{
   let timer;
   const events=['loadedmetadata','loadeddata','canplay','seeked','error'];
   const clean=()=>{events.forEach(e=>video.removeEventListener(e,check));clearTimeout(timer);waiting.delete(abort)};
   const finish=value=>{clean();resolve(value)};
   const abort=()=>finish(false);
   const check=()=>{
    if(token!==epoch){finish(false);return}
    if(video.error){clean();reject(new Error(video.error.message||'Video decoding failed.'));return}
    if(predicate())finish(true);
   };
   events.forEach(e=>video.addEventListener(e,check));waiting.add(abort);
   timer=setTimeout(()=>{clean();reject(new Error('The video did not finish loading or seeking. Try Restart.'))},15000);
   check();
  });
 }
 async function seek(target,resume=false){
  cancel();const token=epoch;
  offset=Math.max(0,Number(target)||0);intent=resume;running=false;message='';state='seeking';pauseMedia();notify();
  if(!media.length){state='paused';intent=false;notify();return}
  try{
   // Seeking may lower readyState until seeked/canplay. Keep intent while waiting;
   // never discard a loop/restart just because the next frame is not decoded yet.
   const metadata=await Promise.all(media.map(v=>waitFor(v,()=>v.readyState>=1&&Number.isFinite(v.duration),token)));
   if(token!==epoch||metadata.some(ok=>!ok))return;
   offset=Math.min(offset,duration());
   if(resume&&offset>=duration()-.002)offset=0;
   for(const video of media){
    const targetTime=Math.min(offset,Math.max(0,video.duration-.001));
    if(Math.abs(video.currentTime-targetTime)>.002)video.currentTime=targetTime;
   }
   const frames=await Promise.all(media.map(v=>waitFor(v,()=>v.readyState>=2&&!v.seeking,token)));
   if(token!==epoch||frames.some(ok=>!ok))return;
   if(!resume){state='paused';notify();return}
   state='starting';notify();
   const results=await Promise.allSettled(media.filter(v=>offset<v.duration-.002).map(v=>v.play()));
   if(token!==epoch||!intent)return;
   const rejected=results.find(r=>r.status==='rejected');
   if(rejected)throw rejected.reason;
   running=true;state='playing';notify();
  }catch(error){if(token===epoch)fail(error)}
 }
 function play(){return seek(time(),true)}
 function finish(){
  if(!intent||!running)return;
  if(hooks.repeat?.())seek(0,true);
  else{offset=duration();cancel();intent=false;running=false;pauseMedia();state='ended';notify()}
 }
 function setRate(value){const current=time(),resume=intent;rate=Number(value);allMedia.forEach(v=>v.playbackRate=rate);return seek(current,resume)}
 function setMedia(next){pause();media=[...next];offset=0;message='';state='paused';media.forEach(v=>v.playbackRate=rate);notify()}
 function tick(){
  if(!running)return;
  const lead=leader();
  if(lead?.ended){finish();return}
  const current=time();
  for(const video of media){
   if(video===lead||video.seeking||video.readyState<2)continue;
   const target=Math.min(current,Math.max(0,video.duration-.001));
   if(Math.abs(video.currentTime-target)>.075)video.currentTime=target;
  }
 }
 for(const video of allMedia){
  video.addEventListener('ended',()=>{if(video===leader()&&video.ended)finish()});
  video.addEventListener('waiting',()=>{if(media.includes(video)&&running)seek(time(),true)});
  video.addEventListener('error',()=>{if(media.includes(video)&&video.error)fail(video.error)});
 }
 return {play,pause,seek,restart:()=>seek(0,true),setRate,setMedia,tick,time,duration,
  get intent(){return intent},get state(){return state},get message(){return message}};
}
// END TRANSPORT
let clip=DATA.names.includes('hatchet_chop')?'hatchet_chop':DATA.names[0],view=DATA.kinds[0],dragging=false,resumeAfterDrag=false;
const record=()=>DATA.clips[clip][view];
const selected=()=>videos.filter(v=>record()[v.id].video);
const sec=n=>Number(n).toFixed(3);
const transport=createTransport(videos,{change:renderStatus,repeat:()=>$('repeat').checked});
function setLink(id,media){$(id).hidden=!media;if(media)$(id).href=media.url;else $(id).removeAttribute('href')}
function renderStatus(){
 const errors=selected().filter(v=>failures.has(v.id));
 const loading=selected().filter(v=>v.readyState<2&&!failures.has(v.id));
 let message='';
 if(transport.message)message=transport.message;
 else if(errors.length)message=errors.map(v=>(v.id==='baseline'?'Blender v3':'Cascadeur')+': '+failures.get(v.id)).join(' · ');
 else if(['seeking','starting'].includes(transport.state))message=transport.intent?'Preparing synchronized playback…':'Seeking…';
 else if(loading.length)message='Loading '+loading.map(v=>v.id==='baseline'?'Blender v3':'Cascadeur').join(' and ')+' video…';
 else if(!record().candidate.video)message=record().candidate.still?'Candidate still available; waiting for candidate animation video.':record().baseline.video?'Awaiting Cascadeur candidate. The preserved Blender baseline is available.':'This new weapon clip is being prepared; neither a candidate video nor a v3 baseline is available.';
 else message=record().baseline.video?'Both videos loaded. Candidate quality has not been accepted.':'Cascadeur candidate loaded. No Blender v3 baseline exists for this clip; quality has not been accepted.';
 $('status').textContent=message;
 const enabled=selected().length>0&&transport.duration()>0&&!errors.length;
 $('play').disabled=!selected().length;$('restart').disabled=!selected().length;$('seek').disabled=!enabled;
 $('beat').disabled=!enabled||DATA.clips[clip].beat===null;
 const target=selected().length===2?' both':record().candidate.video?' candidate':record().baseline.video?' baseline':'';
 $('play').textContent=(transport.intent?'Pause':'Play')+target;
 $('seek').max=String(transport.duration()||1);
}
function load(name=clip,kind=view){
 transport.setMedia([]);clip=name;view=kind;failures.clear();dragging=false;resumeAfterDrag=false;
 for(const name of DATA.names){
  $(name).setAttribute('aria-pressed',String(name===clip));
  const item=DATA.clips[name][view];
  $(name+'-availability').textContent=item.candidate.video?(item.baseline.video?'Compare with v3':'Candidate only'):(item.baseline.video?'Baseline · candidate pending':'Candidate pending');
 }
 keys.forEach(key=>{
  const media=record()[key],video=$(key),image=$(key+'-image'),empty=$(key+'-empty');
  video.hidden=!media.video;image.hidden=Boolean(media.video)||!media.still;empty.hidden=Boolean(media.video||media.still);
  video.removeAttribute('src');video.removeAttribute('poster');
  if(media.still){image.src=media.still.url;video.poster=media.still.url}else image.removeAttribute('src');
  if(media.video)video.src=media.video.url;
  video.load();
  setLink(key+'-still',media.still);setLink(key+'-video',media.video);
 });
 transport.setMedia(selected());
 const candidateOnly=!(record().baseline.video||record().baseline.still)&&Boolean(record().candidate.video||record().candidate.still);
 $('players').classList.toggle('candidate-only',candidateOnly);
 $('baseline-panel').hidden=candidateOnly;
 $('baseline-notice').hidden=!candidateOnly;
 $('candidate-tag').textContent=record().candidate.video?'Unaccepted candidate':record().candidate.still?'Still only':'Awaiting candidate';
 $('baseline-tag').textContent=record().baseline.video?'Preserved baseline':'Not available';
 $('baseline-info').textContent=record().baseline.video?'Blender render · '+view.toUpperCase()+' · verified copy of v3':'There is no Blender v3 baseline for this weapon clip.';
 $('candidate-info').textContent=record().candidate.video||record().candidate.still?record().candidate.rendering_method:'Candidate media has not been generated for this view.';
 $('focus').textContent=DATA.clips[clip].focus;
 $('beat').textContent=DATA.clips[clip].beat_label;
 $('conditions').textContent=record().candidate.notes||'A fair comparison needs the same character, props, camera and timing. Read candidate capture details before judging a rendering difference as a pose difference.';
 renderStatus();
}
videos.forEach(v=>{
 for(const event of ['loadedmetadata','loadeddata','canplay','durationchange'])v.addEventListener(event,renderStatus);
 v.addEventListener('error',()=>{if(!record()[v.id].video)return;failures.set(v.id,v.error?.message||'The browser could not decode this video.');renderStatus()});
});
for(const name of DATA.names)$(name).onclick=()=>load(name);
if($('view'))$('view').onchange=()=>load(clip,$('view').value);
$('play').onclick=()=>transport.intent?transport.pause():transport.play();
$('restart').onclick=()=>transport.restart();
$('beat').onclick=()=>transport.seek(DATA.clips[clip].beat,false);
$('speed').onchange=()=>transport.setRate($('speed').value);
$('seek').addEventListener('pointerdown',()=>{resumeAfterDrag=transport.intent});
$('seek').addEventListener('input',()=>{dragging=true;transport.seek($('seek').value,false)});
$('seek').addEventListener('change',()=>{dragging=false;if(resumeAfterDrag)transport.play();resumeAfterDrag=false});
function tick(){
 transport.tick();const time=transport.time();
 if(!dragging)$('seek').value=String(time);
 $('time').textContent=sec(time)+' / '+sec(transport.duration())+' s';
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
    parser.add_argument("--kinds", nargs="+", choices=("fps", "tps"), default=list(KINDS))
    args = parser.parse_args(argv)
    kinds = tuple(dict.fromkeys(args.kinds))
    baseline, out = args.baseline.resolve(), args.out.resolve()
    review = out / "review"
    if review == baseline or baseline in review.parents or review in baseline.parents:
        raise ValueError("Comparison review must be separate from the preserved baseline")
    candidate = out / "candidates"
    metadata_path = candidate / "metadata.json"
    metadata = json.loads(metadata_path.read_text(encoding="utf-8")) if metadata_path.is_file() else {}
    data = {"generated_utc": datetime.now(timezone.utc).isoformat(), "names": list(NAMES), "kinds": list(kinds),
            "baseline": str(baseline), "candidate_input": str(candidate), "clips": {}}
    for name in NAMES:
        beat = 0.6 if name == "hatchet_chop" else 1.1 if name == "torch_equip" else None
        data["clips"][name] = {"title": TITLES[name], "focus": FOCUS[name], "beat": beat,
                               "beat_label": "Jump to hold" if name == "torch_equip" else "Jump to contact"}
        for kind in kinds:
            details = metadata.get("clips", {}).get(kind, {}).get(name, {})
            if "beat" in details:
                data["clips"][name]["beat"] = details["beat"]
                data["clips"][name]["beat_label"] = details.get("beat_label", "Jump to contact")
            entry = {}
            for label in ("baseline", "candidate"):
                source_video = baseline / "videos" / kind / f"{name}.mp4" if label == "baseline" else candidate / kind / f"{name}.mp4"
                source_still = baseline / "review" / kind / f"{name}.png" if label == "baseline" else candidate / kind / f"{name}.png"
                target = review / "media" / label / kind
                entry[label] = {"video": copy_media(source_video, target / f"{name}.mp4", review),
                                "still": copy_media(source_still, target / f"{name}.png", review)}
                if label == "baseline" and name in ("hatchet_chop", "torch_equip") and not entry[label]["video"]:
                    raise FileNotFoundError(f"Preserved baseline video missing: {source_video}")
            conditions = COMPARISON_CONDITIONS if name in ("hatchet_chop", "torch_equip") and kind == "fps" else "This is a new weapon animation with no Blender v3 baseline. Inspect the native Cascadeur viewport motion; visual quality has not been accepted."
            entry["candidate"].update({"rendering_method": str(details.get("rendering_method", f"Native Cascadeur viewport frames · {kind.upper()}")),
                                       "notes": str(details.get("notes", conditions)), "accepted": False})
            data["clips"][name][kind] = entry
    review.mkdir(parents=True, exist_ok=True)
    (review / "manifest.json").write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    payload = json.dumps(data, ensure_ascii=True).replace("<", "\\u003c")
    buttons = "".join(f'<button id="{name}" aria-pressed="false">{TITLES[name]}<small id="{name}-availability"></small></button>' for name in NAMES)
    view_controls = '<span class="view-choice tag">First-person weapons · FPS</span>' if kinds == ("fps",) else '<label class="view-choice">View <select id="view" aria-label="Camera view">' + "".join(f'<option value="{kind}">{kind.upper()}</option>' for kind in kinds) + '</select></label>'
    (review / "index.html").write_text(HTML.replace("__DATA__", payload).replace("__CLIP_BUTTONS__", buttons).replace("__VIEW_CONTROLS__", view_controls), encoding="utf-8")
    count = sum(bool(data["clips"][name][kind]["candidate"]["video"]) for name in NAMES for kind in kinds)
    print(json.dumps({"review": str(review / "index.html"), "candidate_videos": count, "expected_candidate_videos": len(NAMES) * len(kinds)}))


if __name__ == "__main__":
    main()
