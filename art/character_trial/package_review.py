"""Create a portable, dependency-free review page and honest package manifest.

python art/character_trial/package_review.py --out .artifacts/character-trial-v2

Writes index.html, media-status.json, manifest.json and README.md in the output.
Use --status-only after an encode finishes to refresh media availability cheaply.
Does not render, encode, copy assets, change animation sources or create a ZIP.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import re
from urllib.parse import urlsplit


ROOT = Path(__file__).resolve().parents[2]
SOURCE_FILES = (
    'art/character_trial/build.py', 'art/character_trial/animate.py',
    'art/character_trial/motions.py', 'art/character_trial/fps_motions.py',
    'art/character_trial/props.py', 'art/character_trial/grip_profile.json',
    'art/character_trial/review.py', 'art/character_trial/encode_review.py', 'art/character_trial/validate.py',
    'art/character_trial/validate_animation.py', 'art/character_trial/package_review.py',
    'art/character_trial/serve_review.py',
    'art/character_trial/refine_fps.py',
    'art/characters/authored.py', 'art/lib/hollow_art.py',
)


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


def load_clips(path):
    clips = json.loads(path.read_text(encoding='utf-8'))
    if isinstance(clips, dict):
        clips = clips.get('clips')
    if not isinstance(clips, list) or len(clips) != 8:
        raise ValueError(f'{path.name} must contain exactly eight clips')
    names = set()
    for clip in clips:
        name = clip.get('name', '')
        if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]*', name) or name in names:
            raise ValueError(f'Unsafe or duplicate clip name: {name!r}')
        names.add(name)
        duration = float(clip['duration'])
        if not math.isfinite(duration) or duration <= 0:
            raise ValueError(f'Invalid duration for {name}')
    return clips


HTML = r'''<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Survivor · Motion review</title>
<style>
:root{color-scheme:dark;font:15px/1.5 system-ui,sans-serif;background:#11171b;color:#e6e9e9}
*{box-sizing:border-box}[hidden]{display:none!important}body{margin:0}main{max-width:1500px;margin:auto;padding:32px 26px 46px}
h1{font-size:30px;letter-spacing:-.7px;margin:0 0 6px}h2{font-size:16px;margin:0}
.eyebrow{font-size:11px;letter-spacing:2px;text-transform:uppercase;color:#91b1b1;margin-bottom:9px}
.muted{color:#a7b3ba}.intro{max-width:850px;margin:0 0 25px}.top{display:flex;align-items:start;justify-content:space-between;gap:20px}
.revision-row{display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap;margin:-8px 0 22px;font-size:13px}.revision-row span{color:#b3dfd2}
a{color:#96d7c7;text-underline-offset:3px}nav{display:grid;grid-template-columns:repeat(8,1fr);gap:7px;margin-bottom:24px}
button,select{font:inherit;color:inherit;background:#253037;border:1px solid #45525c;border-radius:7px;padding:9px 14px;cursor:pointer}
button:hover{background:#33444b}button:focus-visible,select:focus-visible,a:focus-visible{outline:3px solid #9ce4d2;outline-offset:3px}
nav button{text-align:left;padding:12px 10px;font-size:13px}nav button small{display:block;color:#a9b8c0;font-size:11px;margin-bottom:4px}
nav button[aria-pressed=true]{border-color:#a3dbcc;background:#24463e;color:#e2fff5}
.players{display:grid;grid-template-columns:1fr 1fr;gap:18px}.panel{border:1px solid #354149;border-radius:11px;overflow:hidden;background:#1a2329}
.panel header{padding:15px 17px;display:flex;justify-content:space-between;align-items:center;gap:12px}.tag{font-size:11px;color:#b1c1c9;border:1px solid #4b5c65;border-radius:20px;padding:3px 9px}
video,.preview{display:block;width:100%;aspect-ratio:896/630;background:#0b1013;object-fit:contain}.preview-empty{display:grid;place-items:center;color:#a7b3ba;width:100%;aspect-ratio:896/630;background:#0b1013}
.panel footer{padding:10px 17px;display:flex;justify-content:space-between;gap:10px;font-size:12px;color:#b3c0c7;min-height:40px}
.transport{margin-top:18px;display:flex;align-items:center;gap:9px;flex-wrap:wrap}.primary{background:#a1d8c7;border-color:#a1d8c7;color:#12261f;font-weight:650}.primary:hover{background:#c1eddf}
label{display:flex;align-items:center;gap:8px}.repeat{margin-left:auto;font-size:13px}.details{margin-top:20px;border-top:1px solid #354149;padding-top:18px;display:grid;grid-template-columns:1fr 1fr;gap:18px}
.details p{margin:4px 0 0;color:#b1bec5;font-size:13px}.links{display:flex;flex-wrap:wrap;gap:17px;margin-top:26px;font-size:13px}.status{font-size:13px;min-height:22px;color:#e5c29b;margin:12px 0 0}
.note{font-size:12px;color:#93a5ae;max-width:900px;margin-top:20px}button:disabled{opacity:.4;cursor:not-allowed}
@media(max-width:900px){nav{grid-template-columns:repeat(4,1fr)}.top{display:block}}
@media(max-width:650px){main{padding:23px 14px}.players,.details{grid-template-columns:1fr}.repeat{margin-left:0}h1{font-size:26px}nav button{font-size:12px}}
</style>
<main>
 <div class="top"><div><div class="eyebrow">Hollow Universe · isolated character audition</div>
 <h1>Eight motions. Two perspectives.</h1>
 <p class="intro muted">Inspect the third-person character and separately authored first-person arms together. Slow the clips down to check grip, wrist rotation, elbows, foot contact and recovery.</p></div></div>
 <div class="revision-row" id="review-context" hidden><span id="revision" hidden></span><a id="baseline" target="_blank" rel="noopener" hidden>Open v2 baseline</a></div>
 <nav id="clips" aria-label="Choose animation"></nav>
 <div class="players">
  <section class="panel"><header><h2>Third person</h2><span class="tag">Full character · TPS</span></header>
   <video id="tps" controls playsinline preload="metadata" aria-label="Third-person animation"></video>
   <img id="tps-preview" class="preview" alt="Third-person still preview" hidden>
   <div id="tps-empty" class="preview-empty" hidden>Preview not available yet</div>
   <footer><span id="tps-info"></span><a id="tps-still">Open still</a></footer></section>
  <section class="panel"><header><h2>First person</h2><span class="tag">Separate arms · FPS</span></header>
   <video id="fps" controls playsinline preload="metadata" aria-label="First-person animation"></video>
   <img id="fps-preview" class="preview" alt="First-person still preview" hidden>
   <div id="fps-empty" class="preview-empty" hidden>Preview not available yet</div>
   <footer><span id="fps-info"></span><a id="fps-still">Open still</a></footer></section>
 </div>
 <div class="transport" aria-label="Synchronized playback controls">
  <button class="primary" id="play">Play both</button><button id="restart">Restart</button>
  <button id="beat">Jump to contact</button>
  <label>Speed <select id="speed" aria-label="Playback speed"><option value=".25">0.25×</option><option value=".5">0.5×</option><option value="1" selected>1×</option></select></label>
  <label class="repeat"><input type="checkbox" id="repeat" checked> Repeat clip</label>
 </div>
 <p class="status" id="status" role="status" aria-live="polite"></p>
 <div class="details"><div><h2 id="name"></h2><p id="description"></p></div><div><h2>Timing</h2><p id="timing"></p></div></div>
 <div class="links" id="downloads"></div>
 <p class="note">This package is an isolated review candidate. Successful export or numeric validation does not establish visual acceptance. These videos show the baked Blender actions; they do not prove runtime Godot import or gameplay behavior. Native video controls also synchronize the paired clip.</p>
</main>
<script>
const DATA=__REVIEW_DATA__;
const $=id=>document.getElementById(id), kinds=['tps','fps'], videos=kinds.map($);
if(DATA.revision){$('revision').textContent=DATA.revision.label+' · '+DATA.revision.note;$('revision').hidden=false}
if(DATA.baseline_url){$('baseline').href=DATA.baseline_url;$('baseline').hidden=false}
$('review-context').hidden=!DATA.revision&&!DATA.baseline_url;
let chosen=0,changing=false,generation=0,polling=false,playIntent=false,transportVersion=0,loopRestart=false;
const commanded=new Map(videos.map(v=>[v,{pauses:0,seek:null,rate:null}]));
const title=name=>name.split('_').map(w=>w[0].toUpperCase()+w.slice(1)).join(' ');
const sec=n=>Number(n).toFixed(3).replace(/0+$/,'').replace(/\.$/,'')+' s';
const contacts=clip=>Array.isArray(clip.contact)?clip.contact:(typeof clip.contact==='number'?[clip.contact]:[]);
const current=()=>DATA.clips[chosen];
const available=()=>videos.filter((v,i)=>current()[kinds[i]].video_exists&&!v.hidden);
const isPlaying=()=>available().some(v=>!v.paused&&!v.ended);
const setStatus=message=>$('status').textContent=message;
function refreshPlay(){
 const count=available().length,verb=isPlaying()?'Pause':'Play';
 $('play').textContent=count===2?verb+' both':count===1?verb+' '+available()[0].id.toUpperCase():'Video rendering';
 $('play').disabled=!count;$('restart').disabled=!count;
 $('beat').disabled=!count||!contacts(current().tps.meta).length;
}
function mediaStatus(){
 const missing=kinds.filter(kind=>!current()[kind].video_exists);
 setStatus(missing.length?missing.map(kind=>kind.toUpperCase()).join(' and ')+' video rendering — still preview. Videos appear here automatically as they finish.':'');
}
function commandPause(video){
 // Media events are queued tasks: a synchronous boolean guard cannot suppress them.
 if(!video.paused){commanded.get(video).pauses++;video.pause()}
}
function commandSeek(video,time){
 if(!video.readyState)return;
 const duration=Number.isFinite(video.duration)?video.duration:time,target=Math.max(0,Math.min(time,duration));
 if(Math.abs(video.currentTime-target)<.001)return;
 commanded.get(video).seek=target;video.currentTime=target;
}
function commandRate(video,rate){
 if(video.playbackRate===rate)return;
 commanded.get(video).rate=rate;video.playbackRate=rate;
}
function pause(){playIntent=false;++transportVersion;videos.forEach(commandPause);refreshPlay()}
function seek(time){available().forEach(v=>commandSeek(v,time))}
async function play(){
 const playable=available();if(!playable.length){mediaStatus();refreshPlay();return}
 playIntent=true;const request=++transportVersion,clipGeneration=generation;
 const results=await Promise.allSettled(playable.map(v=>v.play()));
 // pause(), clip changes and later play requests deliberately abort older promises.
 if(request!==transportVersion||clipGeneration!==generation||!playIntent)return;
 const failed=results.find(r=>r.status==='rejected');
 if(failed)setStatus(failed.reason&&failed.reason.name==='AbortError'?'Playback was interrupted. Press Play to resume.':'The browser could not start a video. Use its native play control or open the MP4 link below.');
 else mediaStatus();refreshPlay();
}
function syncFrom(source){
 if(changing)return;
 const other=available().find(v=>v!==source);
 if(other&&other.readyState&&Math.abs(other.currentTime-source.currentTime)>.035)commandSeek(other,source.currentTime);
}
videos.forEach(source=>{
 source.addEventListener('play',()=>{
  if(changing||source.hidden||source.paused)return;
  if(!playIntent){syncFrom(source);play()} // A native Play starts the pair once.
  refreshPlay();
 });
 source.addEventListener('pause',()=>{
  const pending=commanded.get(source);if(pending.pauses){pending.pauses--;return}
  if(changing||source.ended||source.hidden||!source.paused||!playIntent)return;
  pause(); // Only an uncommanded native Pause changes the shared intent.
 });
 source.addEventListener('seeking',()=>{
  const pending=commanded.get(source);
  if(pending.seek!==null&&Math.abs(source.currentTime-pending.seek)<.08)return;
  pending.seek=null;syncFrom(source);
 });
 source.addEventListener('seeked',()=>{commanded.get(source).seek=null});
 source.addEventListener('ratechange',()=>{
  const pending=commanded.get(source);
  if(pending.rate===source.playbackRate){pending.rate=null;return}
  pending.rate=null;if(changing)return;
  videos.forEach(v=>commandRate(v,source.playbackRate));$('speed').value=String(source.playbackRate);
 });
 source.addEventListener('ended',()=>{
  // Only the first available perspective owns loop boundaries. A late follower
  // ended event must never rewind a loop that the leader has already restarted.
  if(changing||source!==available()[0]||!source.ended||!playIntent||loopRestart)return;
  if(!$('repeat').checked){pause();return}
  loopRestart=true;seek(0);play().finally(()=>{loopRestart=false});
 });
 source.addEventListener('error',()=>{if(!changing&&!source.hidden)setStatus('A video could not be loaded. Check that the videos folder accompanies this page, or open the MP4 directly.');});
});
function link(label,path){const a=document.createElement('a');a.textContent=label;a.href=path;a.download='';return a}
function metadataReady(video){
 if(video.readyState>=1||!video.hasAttribute('src'))return Promise.resolve();
 return new Promise(resolve=>{let timer;const done=()=>{clearTimeout(timer);video.removeEventListener('loadedmetadata',done);video.removeEventListener('error',done);resolve()};video.addEventListener('loadedmetadata',done);video.addEventListener('error',done);timer=setTimeout(done,10000)});
}
function refreshDownloads(){
 const clip=current(),downloads=$('downloads');downloads.replaceChildren();
 DATA.downloads.forEach(d=>downloads.appendChild(link(d.label,d.path)));
 kinds.forEach(kind=>{if(clip[kind].video_exists)downloads.appendChild(link(kind.toUpperCase()+' MP4',clip[kind].video_url||clip[kind].video))});
 downloads.appendChild(link('Manifest','manifest.json'));downloads.appendChild(link('Readme','README.md'));
}
function showAsset(kind){
 const asset=current()[kind],v=$(kind),preview=$(kind+'-preview'),empty=$(kind+'-empty');
 v.hidden=!asset.video_exists;preview.hidden=asset.video_exists||!asset.still_exists;empty.hidden=asset.video_exists||asset.still_exists;
 const stillUrl=asset.still_url||asset.still,videoUrl=asset.video_url||asset.video;
 if(asset.still_exists){preview.src=stillUrl;preview.alt=title(current().name)+' · '+kind.toUpperCase()+' still preview';v.poster=stillUrl}else{preview.removeAttribute('src');v.removeAttribute('poster')}
 if(asset.video_exists)v.src=videoUrl;else v.removeAttribute('src');
 commandPause(v);v.load();commandRate(v,Number($('speed').value));
 $(kind+'-info').textContent=asset.video_exists?sec(asset.meta.duration)+' · '+(asset.meta.fps||30)+' fps source':'Video rendering — still preview';
 const still=$(kind+'-still');still.href=stillUrl;still.hidden=!asset.still_exists;
}
function choose(index){
 const wasPlaying=playIntent||isPlaying();chosen=index;const ticket=++generation;changing=true;pause();loopRestart=false;
 const clip=current();$('name').textContent=title(clip.name);$('description').textContent=clip.tps.meta.description||'';
 const beats=contacts(clip.tps.meta);$('beat').disabled=!beats.length;
 $('timing').textContent='TPS '+sec(clip.tps.meta.duration)+' · FPS '+sec(clip.fps.meta.duration)+' · '+(clip.tps.meta.loop?'Authored loop':'Single action')+(beats.length?' · Contact '+beats.map(sec).join(', '):' · No marked contact');
 $('clips').querySelectorAll('button').forEach((b,i)=>b.setAttribute('aria-pressed',String(i===index)));
 kinds.forEach(showAsset);refreshDownloads();mediaStatus();refreshPlay();
 Promise.all(available().map(metadataReady)).then(()=>{if(ticket!==generation)return;changing=false;if(available().some(v=>v.error))setStatus('A video could not be loaded. Open its MP4 link directly.');else if(wasPlaying)play()});
}
async function refreshAvailability(){
 if(polling||changing||!/^https?:$/.test(location.protocol)||!DATA.clips.some(c=>kinds.some(k=>!c[k].video_exists)))return;
 polling=true;
 try{
  const response=await fetch('media-status.json?at='+Date.now(),{cache:'no-store'});if(!response.ok)return;
  const update=await response.json();if(!Array.isArray(update.clips))return;
  const selected=current(),changed=[];
  for(const remote of update.clips){
   const clip=DATA.clips.find(c=>c.name===remote.name);if(!clip)continue;
   for(const kind of kinds){
    const item=remote[kind];if(!item)continue;
    const different=clip[kind].video_exists!==item.video_exists||clip[kind].still_exists!==item.still_exists||clip[kind].video_url!==item.video_url||clip[kind].still_url!==item.still_url;
    if(typeof item.video_exists==='boolean')clip[kind].video_exists=item.video_exists;
    if(typeof item.still_exists==='boolean')clip[kind].still_exists=item.still_exists;
    if(typeof item.video_url==='string')clip[kind].video_url=item.video_url;
    if(typeof item.still_url==='string')clip[kind].still_url=item.still_url;
    if(different&&clip===selected)changed.push(kind);
   }
  }
  if(!changed.length)return;
  const ticket=generation,leader=available().find(v=>!v.paused)||available()[0],time=leader?leader.currentTime:0,wasPlaying=isPlaying();
  // Keep the already-playing perspective untouched while its new partner loads.
  changing=true;changed.forEach(showAsset);refreshDownloads();mediaStatus();refreshPlay();
  await Promise.all(changed.map(kind=>metadataReady($(kind))));if(ticket!==generation)return;
  changed.forEach(kind=>{const v=$(kind);if(!v.hidden&&v.readyState)commandSeek(v,leader&&!leader.hidden?leader.currentTime:time)});
  changing=false;if(wasPlaying)await play();else refreshPlay();
 }catch(error){/* A transient server restart leaves the current previews usable. */}
 finally{polling=false}
}
DATA.clips.forEach((clip,index)=>{
 const b=document.createElement('button'),number=document.createElement('small');number.textContent=String(index+1).padStart(2,'0');
 b.append(number,document.createTextNode(title(clip.name)));b.type='button';b.onclick=()=>choose(index);$('clips').appendChild(b);
});
$('play').onclick=()=>isPlaying()?pause():play();
$('restart').onclick=()=>{seek(0);play()};
$('beat').onclick=()=>{pause();const beat=contacts(current().tps.meta)[0];if(beat!==undefined)seek(beat)};
$('speed').onchange=()=>videos.forEach(v=>commandRate(v,Number($('speed').value)));
// Correct occasional decoder drift without seeking every frame.
setInterval(()=>{if(!changing&&!loopRestart&&available().length===2&&!videos[0].paused&&!videos[1].paused&&videos.every(v=>v.readyState>=3&&!v.seeking)&&Math.abs(videos[0].currentTime-videos[1].currentTime)>.10)commandSeek(videos[1],videos[0].currentTime)},250);
setInterval(refreshAvailability,5000);
choose(0);
</script></html>
'''


def media_entries(out):
    out = Path(out).resolve()
    if not out.is_dir():
        raise ValueError(f'Output directory does not exist: {out}')
    tps = load_clips(out / 'clips.json')
    fps = load_clips(out / 'fps-clips.json')
    fps_by_name = {c['name']: c for c in fps}
    if {c['name'] for c in tps} != set(fps_by_name):
        raise ValueError('TPS and FPS clip names must match')
    clip_entries = []
    expected = ['survivor-tps.glb', 'survivor-fps.glb', 'Survivor-Audition.blend',
                'clips.json', 'fps-clips.json']
    for clip in tps:
        entry = {'name': clip['name']}
        for kind, meta in (('tps', clip), ('fps', fps_by_name[clip['name']])):
            video = f'videos/{kind}/{clip["name"]}.mp4'
            still = f'review/{kind}/{clip["name"]}.png'
            entry[kind] = {'meta': meta, 'video': video, 'still': still,
                           'video_exists': (out / video).is_file(),
                           'still_exists': (out / still).is_file()}
            expected.extend((video, still))
        clip_entries.append(entry)
    missing = [name for name in expected if not (out / name).is_file()]
    return out, tps, clip_entries, missing


def media_url(out, relative):
    path = out / relative
    return relative + '?v=' + digest(path) if path.is_file() else relative


def write_media_status(out, clip_entries=None):
    if clip_entries is None:
        out, _, clip_entries, _ = media_entries(out)
    status = {
        'updated_utc': datetime.now(timezone.utc).isoformat(),
        'clips': [{'name': clip['name'], **{
            kind: {**{key: clip[kind][key] for key in ('video_exists', 'still_exists')},
                   'video_url': media_url(out, clip[kind]['video']),
                   'still_url': media_url(out, clip[kind]['still'])}
            for kind in ('tps', 'fps')}} for clip in clip_entries],
    }
    temporary = out / 'media-status.json.tmp'
    temporary.write_text(json.dumps(status, indent=2) + '\n', encoding='utf-8')
    temporary.replace(out / 'media-status.json')
    return status


def refinement_summary(out):
    report_path = out / 'fps-refinement.json'
    if not report_path.is_file():
        return None
    report = json.loads(report_path.read_text(encoding='utf-8'))
    version = re.search(r'(?:^|[-_])(v\d+)$', out.name, re.IGNORECASE)
    return {
        'label': 'FPS revision ' + version[1].lower() if version else 'FPS grip revision',
        'note': 'Revised grips and strike paths; TPS baseline retained.',
        'report': 'fps-refinement.json',
        'baseline': report.get('baseline'),
        'changed_fps_clips': sorted(report.get('after', {})),
        'tps_action_sha256_unchanged': report.get('tps_action_sha256_unchanged'),
    }


def validate_baseline_url(value):
    if value is None:
        return None
    parsed = urlsplit(value)
    if parsed.scheme not in ('http', 'https') or not parsed.netloc or parsed.username or parsed.password:
        raise ValueError('--baseline-url must be an HTTP(S) URL without embedded credentials')
    return value


def create_review(out, baseline_url=None):
    baseline_url = validate_baseline_url(baseline_url)
    out, tps, clip_entries, missing = media_entries(out)
    revision = refinement_summary(out)
    media_status = write_media_status(out, clip_entries)
    media_by_name = {clip['name']: clip for clip in media_status['clips']}
    review_clips = [{**clip, **{kind: {**clip[kind], **media_by_name[clip['name']][kind]}
                              for kind in ('tps', 'fps')}} for clip in clip_entries]
    downloads = [{'label': label, 'path': path} for label, path in (
        ('TPS GLB', 'survivor-tps.glb'), ('FPS arms GLB', 'survivor-fps.glb'),
        ('Blender source', 'Survivor-Audition.blend')) if (out / path).is_file()]
    data = {'clips': review_clips, 'downloads': downloads, 'missing': missing,
            'revision': revision, 'baseline_url': baseline_url}
    payload = json.dumps(data, ensure_ascii=True, allow_nan=False).replace('<', '\\u003c')
    (out / 'index.html').write_text(HTML.replace('__REVIEW_DATA__', payload), encoding='utf-8')

    sources = []
    for relative in SOURCE_FILES:
        path = ROOT / relative
        sources.append({'repository_relative_path': relative, 'present': path.is_file(),
                        **({'bytes': path.stat().st_size, 'sha256': digest(path)} if path.is_file() else {})})
    readme = '''# Survivor motion review

Open `index.html` in a browser. No installation or internet is required.
Keep this page beside `videos/`, `review/`, the GLBs and the Blender file.
For reliable video seeking through a local HTTP address, use the supplied
range-capable server from the repository (Python standard library only):

    python art/character_trial/serve_review.py --root PATH_TO_OUTPUT_DIRECTORY --port 8873

Then open `http://127.0.0.1:8873/index.html`. The server binds only to localhost
and confines file access to that review directory.

Choose one of eight clips to inspect its TPS character and separate FPS arms.
Play, pause, restart, speed changes and native timeline seeks synchronize both
views. Use 0.25× or 0.5× for grip, wrist, elbow, foot contact and recovery review.
“Jump to contact” pauses at the first contact marker; all markers are listed.
When a video is still rendering, its panel shows a labeled PNG preview. An
available TPS or FPS video can play on its own. When served over local HTTP,
the page checks `media-status.json` every five seconds while videos are missing
and loads completed videos without resetting the selected clip. Refresh that
small status file after each completed encode with the command below.
Video, still, poster and MP4 download URLs include their actual SHA-256 hashes,
so a newer candidate cannot reuse cached media from an older package at the same
local address. The manifest retains normal filesystem-relative paths.

If local MP4 playback is unavailable in your browser, open the individual MP4
links with a local video player. The page never uploads assets or calls an
external service. A page opened directly from disk keeps its packaging-time status.

## Contents and status

The intended review set is eight TPS MP4s and eight FPS MP4s, with paired PNG
stills, `survivor-tps.glb`, `survivor-fps.glb` and `Survivor-Audition.blend`.
`manifest.json` records exact file sizes, SHA-256 hashes and missing expected
files. Frame-render folders and temporary backups are excluded from its file list.
The manifest does not certify visual quality. Blender videos do not establish
Godot runtime import, gameplay behavior or user acceptance. Numeric validation
reports, when present, must be read with their stated scope and limitations.

## Provenance

The body mesh, rig weights, skin, eyes, hair, workwear and shoes derive from
MakeHuman / MPFB and the MakeHuman CC0 system asset pack. Asset headers identify
the September 2020 CC0 release by Data Collection AB, Joel Palmius and Jonas
Hauquier. The MPFB add-on is a GPL tool; it is not shipped as character content.
Source pack: `makehuman_system_assets_cc0.zip`, SHA-256
`b542127a8e25547c7c29c19f2d1d2adb9a664c80396ecd694095dbc8028a0107`.
Official source: https://files2.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip
Licence: https://creativecommons.org/publicdomain/zero/1.0/

The rock, hatchet and torch geometry/materials are original project work in
`art/character_trial/props.py`. Motion trajectories and camera composition are
authored for this trial. The existing `art/characters/authored.py` IK solver is
reused. These are not motion-capture recordings or another game's animations.
Local skin sources are 2048-pixel diffuse maps; no scanned skin detail is claimed.

## Reproduction source record

`manifest.json` lists the repository-relative authoring sources and their hashes.
The review generator does not copy those source files or external asset packs.
Full reproduction requires those exact sources, the licensed MakeHuman asset
pack, MPFB, Blender and the commands/flags documented by the authoring scripts.
The output's packed `.blend` and GLBs can be inspected independently.

Recreate only this page and manifest from the repository:

    python art/character_trial/package_review.py --out PATH_TO_OUTPUT_DIRECTORY

This command writes `index.html`, `media-status.json`, `manifest.json` and this `README.md`; it does
not render, re-encode, modify animations or make a ZIP archive.

Refresh only media availability after an encode finishes:

    python art/character_trial/package_review.py --out PATH_TO_OUTPUT_DIRECTORY --status-only
'''
    if revision:
        readme += '''
## FPS revision v3 reproduction

This candidate refines the FPS grips and strike paths using the saved v2 package
as immutable input. It does not rebuild TPS geometry or actions. The refinement
script checks that the TPS action digest is unchanged and records the baseline
path, changed FPS clips and pose diagnostics in `fps-refinement.json`.

Create a separate candidate directory with the recorded authoring sources:

    blender --background --factory-startup --python art/character_trial/refine_fps.py -- --base .artifacts/character-trial-v2 --out PATH_TO_NEW_OUTPUT_DIRECTORY

Never use the v2 baseline directory as `--out`. The v2 packed Blender scene and
its package files are required inputs; the current source alone is not a full
reconstruction of that baseline. Render and encode the changed FPS clips, then
regenerate this page and manifest. Numerical pose diagnostics do not certify
finger contact or visual quality.
'''
    if baseline_url:
        readme += '\n## Baseline comparison\n\nOpen the preserved v2 review at <' + baseline_url + '''>.
This comparison link is optional; the candidate plays without the baseline server.
Retain it when regenerating the page with:

    python art/character_trial/package_review.py --out PATH_TO_OUTPUT_DIRECTORY --baseline-url ''' + baseline_url + '\n'
    if missing:
        readme += '\n## Missing expected files at packaging time\n\n' + ''.join(f'- `{p}`\n' for p in missing)
    (out / 'README.md').write_text(readme, encoding='utf-8')
    files = []
    for path in sorted(out.rglob('*')):
        if not path.is_file():
            continue
        relative = path.relative_to(out)
        if relative.parts[0] == 'frames' or path.name == 'manifest.json' or path.suffix in ('.blend1', '.tmp'):
            continue
        files.append({'path': relative.as_posix(), 'bytes': path.stat().st_size, 'sha256': digest(path)})
    manifest = {
        'schema': 'hollow-character-review-1',
        'created_utc': datetime.now(timezone.utc).isoformat(),
        'purpose': 'Isolated TPS character and separately authored FPS arms motion review',
        'quality_status': 'Candidate for visual review; no acceptance inferred from packaging',
        'revision': revision,
        'baseline_review_url': baseline_url,
        'clip_count_per_perspective': len(tps),
        'expected_video_count': 16,
        'present_video_count': sum(c[k]['video_exists'] for c in clip_entries for k in ('tps', 'fps')),
        'missing_expected_files': missing,
        'clips': clip_entries,
        'files': files,
        'reproduction_sources': sources,
        'provenance': {
            'character_assets': 'MakeHuman/MPFB base mesh, weights and CC0 system assets',
            'asset_license': 'CC0-1.0',
            'source_pack_sha256': 'b542127a8e25547c7c29c19f2d1d2adb9a664c80396ecd694095dbc8028a0107',
            'props': 'Original project geometry/materials: art/character_trial/props.py',
            'motions': 'Original analytic IK choreography with existing authored.py solver',
            'skin_source_resolution': '2048x2048 diffuse; procedural/baked relief is not scanned skin',
        },
        'manifest_hash_scope': 'All listed files; manifest itself, raw frames and temporary backups excluded',
    }
    (out / 'manifest.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=True, allow_nan=False) + '\n', encoding='utf-8')
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', required=True, type=Path)
    parser.add_argument('--status-only', action='store_true', help='Refresh media-status.json after completed encodes')
    parser.add_argument('--baseline-url', help='Optional HTTP(S) link to the preserved v2 review')
    args = parser.parse_args()
    if args.status_only:
        status = write_media_status(args.out)
        print(json.dumps({'status': str(args.out.resolve() / 'media-status.json'),
                          'present_videos': sum(c[k]['video_exists'] for c in status['clips'] for k in ('tps', 'fps'))}))
        return
    manifest = create_review(args.out, baseline_url=args.baseline_url)
    print(json.dumps({'page': str(args.out.resolve() / 'index.html'),
                      'present_videos': manifest['present_video_count'],
                      'expected_videos': manifest['expected_video_count'],
                      'missing_expected_files': manifest['missing_expected_files']}, indent=2))


if __name__ == '__main__':
    main()
