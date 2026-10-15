import assert from 'node:assert/strict';

function makeCtx(log) {
  const param = () => ({ value: 0, setValueAtTime() {}, linearRampToValueAtTime() {}, exponentialRampToValueAtTime() {}, setTargetAtTime() {} });
  const node = (extra = {}) => ({ connect() {}, disconnect() { log.disconnects++; }, ...extra });
  return class {
    constructor() { this.currentTime = 0; this.sampleRate = 8000; this.state = 'suspended'; this.destination = node(); log.ctxs++; }
    resume() { this.state = 'running'; log.resumed++; return Promise.resolve(); }
    close() { log.closed++; return Promise.resolve(); }
    createDynamicsCompressor() { return node({ threshold: param(), ratio: param(), attack: param(), release: param() }); }
    createGain() { return node({ gain: param() }); }
    createBuffer(c, n) { return { getChannelData: () => new Float32Array(n) }; }
    createBiquadFilter() { return node({ frequency: param(), Q: param(), type: '' }); }
    createOscillator() { log.osc++; const o = node({ frequency: param(), detune: param(), type: '', start() {}, stop() { log.stops++; } }); log.oscs.push(o); return o; }
    createBufferSource() { log.osc++; return node({ start() {}, stop() { log.stops++; } }); }
  };
}
const fresh = async () => (await import('../../src/Game.Web/wwwroot/js/audio.js?' + Math.random()));
const mk = () => ({ ctxs: 0, osc: 0, stops: 0, resumed: 0, closed: 0, disconnects: 0, oscs: [] });
const ids = ['dice','hop','buy','sell','build','cash','jail','card','hammer','trade','bankrupt','win','chime'];

// 1. no AudioContext: everything no-ops
{
  delete globalThis.AudioContext; delete globalThis.webkitAudioContext;
  const a = await fresh();
  a.init('{}', '{}'); a.setTheme('{"sound":{"root":220}}'); a.setVolumes(1, 1, 1, false);
  for (const id of ids) a.play(id, 3);
  a.startAmbient(); a.stopAmbient(); a.dispose();
}
// 2. muted => no oscillators; unmute => some
{
  const log = mk(); globalThis.AudioContext = makeCtx(log);
  const a = await fresh();
  a.init('{"sound":{"wave":"sine","root":220}}', '{}');
  a.setVolumes(1, 1, 0.5, true);
  for (const id of ids) a.play(id);
  a.startAmbient();
  assert.equal(log.osc, 0, 'muted creates nothing');
  a.setVolumes(1, 1, 0.5, false);
  a.play('dice'); assert.ok(log.osc > 0, 'unmuted plays');
  a.dispose();
}
// 3. polyphony cap
{
  const log = mk(); globalThis.AudioContext = makeCtx(log);
  const a = await fresh(); a.init('{}', '{}');
  for (let i = 0; i < 50; i++) a.play('cash');
  assert.ok(a._stats().voices <= 12, 'cap ' + a._stats().voices);
  assert.ok(log.stops > 0, 'oldest stopped');
  // 4. dispose cleans
  a.startAmbient(); assert.ok(a._stats().ambient);
  a.dispose();
  assert.equal(a._stats().voices, 0); assert.equal(a._stats().ambient, false); assert.equal(log.closed, 1);
  a.play('buy'); assert.equal(log.ctxs, 1, 'no new ctx after dispose');
}
// 5. setTheme changes root; unknown id and bad intensity do not throw
{
  const log = mk(); globalThis.AudioContext = makeCtx(log);
  const a = await fresh(); a.init('{"sound":{"root":196}}', '{}');
  assert.equal(a._stats().root, 196);
  a.setTheme('{"id":"x","sound":{"wave":"square","root":330}}');
  assert.equal(a._stats().root, 330); assert.equal(a._stats().wave, 'square');
  a.setTheme('not json'); assert.equal(a._stats().root, 330);
  a.play('nope'); a.play('hop', NaN);
  a.dispose();
}
console.log('audio tests passed');
