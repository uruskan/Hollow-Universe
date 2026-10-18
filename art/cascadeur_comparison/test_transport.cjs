// Independent, event-driven media doubles. No browser, server or desktop access.
// Run: node art/cascadeur_comparison/test_transport.cjs
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, 'package_compare.py'), 'utf8');
const code = source.split('// BEGIN TRANSPORT')[1].split('\n').slice(1).join('\n').split('// END TRANSPORT')[0];
const context = vm.createContext({setTimeout, clearTimeout});
vm.runInContext(code + '\nthis.makeTransport=createTransport;', context);

class FakeMedia {
  constructor(name, duration) {
    Object.assign(this, {name, duration, readyState: 4, seeking: false, paused: true,
      ended: false, error: null, playbackRate: 1, playCalls: 0, _time: 0});
    this.listeners = new Map();
  }
  addEventListener(event, callback) {
    if (!this.listeners.has(event)) this.listeners.set(event, new Set());
    this.listeners.get(event).add(callback);
  }
  removeEventListener(event, callback) { this.listeners.get(event)?.delete(callback); }
  emit(event) { for (const callback of [...(this.listeners.get(event) || [])]) callback(); }
  get currentTime() { return this._time; }
  set currentTime(value) {
    this._time = value;
    this.seeking = true;
    this.ended = false;
    this.readyState = 1; // Real seeks can evict the decoded frame temporarily.
    this.emit('seeking');
  }
  finishSeek() {
    this.seeking = false;
    this.readyState = 4;
    this.emit('seeked');
    this.emit('canplay');
  }
  pause() { this.paused = true; }
  play() {
    this.playCalls++;
    if (this.seeking || this.readyState < 2) return Promise.reject(new Error('Started before the seek completed'));
    this.paused = false;
    this.ended = false;
    return Promise.resolve();
  }
  step(seconds) {
    if (this.paused || this.seeking) return;
    this._time = Math.min(this.duration, this._time + seconds * this.playbackRate);
    if (this._time >= this.duration) {
      this.ended = true;
      this.paused = true;
      this.emit('ended');
    }
  }
  newSource(duration) {
    this.pause();
    Object.assign(this, {duration, _time: 0, ended: false, readyState: 1, seeking: false, error: null});
  }
}

const flush = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
async function settle(media) {
  await flush();
  media.forEach(v => v.finishSeek());
  await flush();
}
function fixture(candidate = true) {
  const baseline = new FakeMedia('baseline', 1.4), trial = new FakeMedia('candidate', 43 / 30);
  const result = {baseline, trial, repeat: true};
  result.transport = context.makeTransport([baseline, trial], {repeat: () => result.repeat});
  result.transport.setMedia(candidate ? [baseline, trial] : [baseline]);
  return result;
}

(async () => {
  const passed = [];
  {
    const f = fixture(), t = f.transport;
    await t.play();
    assert.equal(t.state, 'playing');
    for (let loop = 0; loop < 6; loop++) {
      const counts = [f.baseline.playCalls, f.trial.playCalls];
      f.baseline.step(2); f.trial.step(2);
      await flush();
      assert.equal(t.intent, true, 'Repeat must preserve the play request during seeking');
      assert.equal(t.state, 'seeking');
      assert.equal(f.baseline.readyState, 1);
      assert.equal(f.trial.readyState, 1);
      assert.deepEqual([f.baseline.playCalls, f.trial.playCalls], counts, 'No early play before decoded frames return');
      f.baseline.finishSeek(); await flush();
      assert.equal(t.state, 'seeking', 'Both videos must finish seeking');
      f.trial.finishSeek(); await flush();
      assert.equal(t.state, 'playing');
      assert.equal(t.time(), 0);
      assert.deepEqual([f.baseline.playCalls, f.trial.playCalls], counts.map(n => n + 1));
    }
    t.pause();
    passed.push('six repeat loops survive asynchronous readyState drops and unequal durations');
  }
  {
    const f = fixture(), t = f.transport;
    f.repeat = false;
    await t.play();
    f.baseline.step(2); f.trial.step(2); await flush();
    assert.equal(t.state, 'ended');
    assert.equal(t.intent, false);
    assert.equal(t.time(), 43 / 30);
    assert.equal(f.baseline.playCalls, 1);
    t.tick(); await flush();
    assert.equal(f.trial.playCalls, 1);
    const replay = t.play(); await settle([f.baseline, f.trial]); await replay;
    assert.equal(t.state, 'playing');
    assert.equal(t.time(), 0);
    t.pause();
    passed.push('repeat off stops at the final frame; explicit replay resumes from zero');
  }
  {
    const f = fixture(), t = f.transport;
    await t.play();
    for (const rate of [.25, .5, 1]) {
      const before = t.time();
      const changed = t.setRate(rate); await settle([f.baseline, f.trial]); await changed;
      assert.equal(t.time(), before, 'Changing speed must not jump the playhead');
      f.baseline.step(.2); f.trial.step(.2); t.tick();
      assert.ok(Math.abs(t.time() - before - .2 * rate) < 1e-9);
      assert.equal(f.baseline.playbackRate, rate); assert.equal(f.trial.playbackRate, rate);
    }
    t.pause();
    passed.push('0.25x, 0.5x and 1x keep position and use media time rather than wall time');
  }
  {
    const f = fixture(false), t = f.transport;
    await t.play();
    f.baseline.step(2); await settle([f.baseline]);
    assert.equal(t.state, 'playing');
    assert.equal(f.baseline.playCalls, 2);
    assert.equal(f.trial.playCalls, 0);
    t.pause();
    t.setMedia([f.trial]);
    await t.play();
    assert.equal(f.trial.playCalls, 1);
    t.pause();
    t.setMedia([]); await t.play();
    assert.equal(t.state, 'paused'); assert.equal(t.intent, false);
    passed.push('baseline-only, candidate-only and no-media selections are supported');
  }
  {
    const f = fixture(), t = f.transport;
    await t.play();
    const oldSeek = t.seek(.7, true); await flush();
    assert.equal(t.state, 'seeking');
    t.setMedia([]);
    f.baseline.newSource(1.6); f.trial.newSource(1.633333);
    t.setMedia([f.baseline, f.trial]);
    f.baseline.finishSeek(); f.trial.finishSeek(); await oldSeek; await flush();
    assert.equal(t.state, 'paused'); assert.equal(t.intent, false);
    assert.equal(f.baseline.playCalls, 1, 'An old seek must not start the next clip');
    await t.play();
    assert.equal(f.baseline.playCalls, 2);
    const canceled = t.seek(.5, true); await flush(); t.pause();
    await settle([f.baseline, f.trial]); await canceled;
    assert.equal(t.state, 'paused'); assert.equal(t.intent, false);
    assert.equal(f.baseline.playCalls, 2, 'Pause cancels a pending resume');
    passed.push('clip changes and pause cancel obsolete seek-and-resume operations');
  }
  {
    const f = fixture(), t = f.transport;
    await t.play();
    f.baseline.step(.3); f.trial.step(.3);
    f.trial.readyState = 1; f.trial.emit('waiting'); await flush();
    assert.equal(t.state, 'seeking');
    assert.equal(f.baseline.paused, true);
    assert.equal(t.time(), .3);
    f.trial.finishSeek(); await flush();
    assert.equal(t.state, 'playing');
    assert.equal(t.time(), .3);
    t.pause();
    passed.push('buffering pauses the pair and resumes at the same elapsed time');
  }
  {
    const f = fixture(), t = f.transport;
    const pending = t.seek(.4, true); await flush();
    f.trial.error = {message: 'Decoder failed'}; f.trial.emit('error'); await pending;
    assert.equal(t.state, 'error'); assert.equal(t.intent, false);
    assert.equal(t.message, 'Decoder failed');
    t.pause();
    passed.push('decode errors cancel pending playback and expose the failure');
  }
  console.log(JSON.stringify({passed: true, checks: passed.length, coverage: passed,
    limitation: 'Independent event-driven media doubles exercise transport logic; this is not a real-browser codec or playback test.'}, null, 2));
})().catch(error => { console.error(error); process.exitCode = 1; });
