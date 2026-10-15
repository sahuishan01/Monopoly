import assert from 'node:assert/strict';

// Fake AudioContext with a simulated clock: sources fire onended at their stop time.
function makeCtx(log) {
  const param = () => ({ value: 0, setValueAtTime() {}, linearRampToValueAtTime() {}, exponentialRampToValueAtTime() {}, setTargetAtTime() {} });
  const node = (extra = {}) => { const n = { connected: true, connect() {}, disconnect() { n.connected = false; log.disconnects++; }, ...extra }; log.nodes.push(n); return n; };
  const src = (extra) => { const n = node({ start() {}, stop(t) { n.stopAt = t ?? log.now; log.stops++; }, ...extra }); log.sources.push(n); log.osc++; return n; };
  return class {
    constructor() { this.sampleRate = 8000; this.state = 'suspended'; this.destination = node(); log.ctxs++; log.ctx = this; }
    get currentTime() { return log.now; }
    resume() { this.state = 'running'; log.resumed++; return Promise.resolve(); }
    close() { log.closed++; return Promise.resolve(); }
    createDynamicsCompressor() { return node({ threshold: param(), ratio: param(), attack: param(), release: param() }); }
    createGain() { return node({ gain: param() }); }
    createBuffer(c, n) { return { getChannelData: () => new Float32Array(n) }; }
    createBiquadFilter() { return node({ frequency: param(), Q: param(), type: '' }); }
    createOscillator() { return src({ frequency: param(), detune: param(), type: '' }); }
    createBufferSource() { return src({}); }
  };
}
function advance(log, t) {
  log.now = t;
  for (const s of log.sources) if (!s.fired && s.stopAt !== undefined && s.stopAt <= t && s.onended) { s.fired = true; s.onended(); }
}
const fresh = async () => (await import('../../src/Game.Web/wwwroot/js/audio.js?' + Math.random()));
const mk = () => ({ now: 0, ctxs: 0, osc: 0, stops: 0, resumed: 0, closed: 0, disconnects: 0, nodes: [], sources: [] });
const ids = ['dice','hop','buy','sell','build','cash','jail','card','hammer','trade','bankrupt','win','chime'];
const listeners = {};
globalThis.addEventListener = (e, f) => { (listeners[e] ||= new Set()).add(f); };
globalThis.removeEventListener = (e, f) => { listeners[e]?.delete(f); };
const listenerCount = () => Object.values(listeners).reduce((a, s) => a + s.size, 0);

// 1. no AudioContext
{
  delete globalThis.AudioContext; delete globalThis.webkitAudioContext;
  const a = await fresh();
  a.init('{}', '{}'); a.setTheme('{"sound":{"root":220}}'); a.setVolumes(1, 1, 1, false);
  for (const id of ids) a.play(id, 3);
  a.startAmbient(); a.stopAmbient(); a.dispose();
}
// 2. muted => nothing; unmuted => plays
{
  const log = mk(); globalThis.AudioContext = makeCtx(log);
  const a = await fresh();
  a.init('{"sound":{"wave":"sine","root":220}}', '{}');
  a.setVolumes(1, 1, 0.5, true);
  for (const id of ids) a.play(id);
  a.startAmbient();
  assert.equal(log.osc, 0, 'muted creates nothing');
  a.setVolumes(1, 1, 0.5, false);
  assert.ok(a._stats().ambient, 'ambient starts on unmute');
  const before = log.osc;
  a.play('dice'); assert.ok(log.osc > before, 'unmuted plays');
  a.setVolumes(1, 1, 0.5, true);
  assert.equal(a._stats().ambient, false, 'ambient stops on mute');
  a.dispose();
}
// 3. multi-note voices stay connected until the last note ends
for (const id of ['dice', 'win', 'cash', 'bankrupt', 'trade', 'buy', 'chime', 'hammer', 'build', 'jail']) {
  const log = mk(); globalThis.AudioContext = makeCtx(log);
  const a = await fresh(); a.init('{}', '{}');
  a.play(id);
  const mine = log.nodes.filter(n => n !== log.ctx.destination);
  const srcs = log.sources;
  assert.ok(srcs.length >= 1);
  // stopAt is only set when stop() called; our synth passes no stop time, so derive from __end
  for (const s of srcs) s.stopAt = s.__end;
  const lastEnd = Math.max(...srcs.map(s => s.__end));
  const firstEnd = Math.min(...srcs.map(s => s.__end));
  // buses/compressor stay connected; check voice nodes
  const voiceNodes = log.nodes.slice(log.nodes.indexOf(srcs[0]));
  advance(log, lastEnd - 0.001);
  assert.ok(voiceNodes.every(n => n.connected), id + ': all connected before last stop');
  if (srcs.length > 1) assert.ok(firstEnd < lastEnd, id + ' multi-note');
  advance(log, lastEnd + 0.001);
  assert.ok(voiceNodes.every(n => !n.connected), id + ': all disconnected after');
  assert.equal(a._stats().voices, 0, id + ' voice released');
  a.dispose();
}
// 4. polyphony cap + dispose
{
  const log = mk(); globalThis.AudioContext = makeCtx(log);
  const a = await fresh(); a.init('{}', '{}');
  for (let i = 0; i < 50; i++) a.play('cash');
  assert.ok(a._stats().voices <= 12);
  assert.ok(log.stops > 0);
  a.startAmbient(); assert.ok(a._stats().ambient);
  a.dispose();
  assert.equal(a._stats().voices, 0); assert.equal(a._stats().ambient, false); assert.equal(log.closed, 1);
  assert.equal(listenerCount(), 0, 'gesture listeners removed');
  a.play('buy'); assert.equal(log.ctxs, 1);
}
// 5. ambient not created at music=0; started when raised
{
  const log = mk(); globalThis.AudioContext = makeCtx(log);
  const a = await fresh(); a.init('{}', '{}');
  a.setVolumes(1, 1, 0, false); a.startAmbient();
  assert.equal(log.osc, 0); assert.equal(a._stats().ambient, false);
  a.setVolumes(1, 1, 0.6, false); assert.ok(a._stats().ambient);
  a.dispose();
}
// 6. gestures unlock (click/touchend too)
{
  const log = mk(); globalThis.AudioContext = makeCtx(log);
  const a = await fresh(); a.init('{}', '{}');
  assert.ok(listeners.click?.size && listeners.touchend?.size);
  a.dispose();
}
// 7. setTheme
{
  const log = mk(); globalThis.AudioContext = makeCtx(log);
  const a = await fresh(); a.init('{"sound":{"root":196}}', '{}');
  a.setTheme('{"id":"x","sound":{"wave":"square","root":330}}');
  assert.equal(a._stats().root, 330); assert.equal(a._stats().wave, 'square');
  a.setTheme('not json'); assert.equal(a._stats().root, 330);
  a.play('nope'); a.play('hop', NaN);
  a.dispose();
}
console.log('audio tests passed');
