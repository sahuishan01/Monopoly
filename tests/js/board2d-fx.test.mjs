import assert from 'node:assert/strict';

// Minimal DOM stubs: enough for board2d-fx.js to run headless in node.
let hidden = false, rafQ = [], listeners = {};
const rect = { left: 0, top: 0, width: 700, height: 700 };
const el = () => ({
  className: '', style: {}, children: [], classList: { toggle() { } },
  getBoundingClientRect: () => rect, appendChild(c) { this.children.push(c); }, setAttribute() { }, remove() { },
  querySelector: () => null, querySelectorAll: () => [],
  getContext: () => new Proxy({}, { get: () => () => { } }),
});
globalThis.document = {
  get hidden() { return hidden; },
  createElement: () => el(),
  addEventListener: (k, f) => { listeners[k] = f; }, removeEventListener: k => { delete listeners[k]; },
};
globalThis.window = { devicePixelRatio: 1 };
globalThis.matchMedia = () => ({ matches: false });
globalThis.getComputedStyle = () => ({ getPropertyValue: () => '' });
globalThis.requestAnimationFrame = f => { rafQ.push(f); return rafQ.length; };
globalThis.cancelAnimationFrame = () => { rafQ = []; };
const { create, capFor, hopMs, arc, CAP_LOW } = await import('../../src/Game.Web/wwwroot/js/board2d-fx.js');

assert.equal(capFor('low', false), 40);
assert.ok(capFor('high', true) > capFor('auto', true));
assert.ok(hopMs(1) <= 150 && hopMs(12) >= 60 && hopMs(0) === 0);
assert.deepEqual(arc([0, 0], [10, 0], 5, 0), [0, 0]);
assert.deepEqual(arc([0, 0], [10, 0], 5, 1), [10, 0]);
assert.ok(arc([0, 0], [10, 0], 5, .5)[1] < 0, 'arc bulges up');

const root = el(); root.querySelector = s => (s === '.board2d' ? el() : null);
const fx = create(root, '{"quality":"low","currency":"₹"}');
fx.popup = () => { };
for (let i = 0; i < 10; i++) fx.coins([10, 10], [90, 90], 24, 100);
assert.equal(fx.activeCount().particles, CAP_LOW, 'low quality caps particles at 40');
assert.ok(fx.activeCount().looping);

// the loop stops once particles expire
let now = 0;
while (rafQ.length && now < 10000) { const f = rafQ.shift(); now += 50; f(now); }
assert.equal(fx.activeCount().particles, 0); assert.equal(fx.activeCount().looping, false);

// hidden tab: nothing spawns
hidden = true; fx.confetti(); assert.equal(fx.activeCount().particles, 0); hidden = false;

// reduced motion: nothing spawns
fx.setOptions('{"reduceMotion":true}'); fx.dust([50, 50]); assert.equal(fx.activeCount().particles, 0);
fx.setOptions('{"reduceMotion":false}'); fx.dust([50, 50]); assert.ok(fx.activeCount().particles > 0);

// dispose clears and detaches
fx.dispose();
assert.equal(fx.activeCount().particles, 0); assert.equal(fx.activeCount().looping, false);
assert.equal(listeners.visibilitychange, undefined);
fx.confetti(); assert.equal(fx.activeCount().particles, 0, 'no work after dispose');

// ---- hops: only a completed, current hop may write the pawn's inline position ----
function fakeAnim(list) {
  let res, rej;
  const a = { finished: new Promise((r, j) => { res = r; rej = j; }), state: 'running' };
  a.finished.catch(() => { });
  a.cancel = () => { // like WAAPI: cancelling a finished (fill: forwards) animation removes it without rejecting
    if (a.state === 'cancelled') return; const was = a.state; a.state = 'cancelled'; list.splice(list.indexOf(a), 1); if (was === 'running') rej(new Error('AbortError')); };
  a.complete = () => { if (a.state !== 'running') return; a.state = 'finished'; res(a); };
  list.push(a);
  return a;
}
function pawnStub() {
  const anims = [], bodyAnims = [], writes = [];
  const style = new Proxy({}, { set(t, k, v) { if (k === 'left' || k === 'top') writes.push([k, v]); t[k] = v; return true; } });
  const body = { animate: () => fakeAnim(bodyAnims), getAnimations: () => [...bodyAnims] };
  const pawn = { style, offsetWidth: 1, animate: () => fakeAnim(anims), getAnimations: () => [...anims], querySelector: () => body };
  return { pawn, anims, writes };
}
function hopRig() {
  const s = pawnStub();
  const layer = { querySelector: q => (q === '[data-pawn="0"]' ? s.pawn : null) };
  const r = el(); r.querySelector = q => (q === '.board2d' ? el() : q === '.pawns' ? layer : null);
  return { fx: create(r, '{}'), ...s };
}
const tick = () => new Promise(r => setTimeout(r, 0));
const P1 = [[10, 90], [20, 90]], P2 = [[20, 90], [30, 90]];
{ // normal completion writes the final spot
  const { fx, anims, writes } = hopRig();
  const done = fx.hop(0, P1, 1, 0, false);
  anims[0].complete();
  assert.equal(await done, true);
  assert.deepEqual(writes, [['left', '20%'], ['top', '90%']]);
  assert.equal(anims.length, 0, 'forwards animation released');
  fx.dispose();
}
{ // snap() mid-hop: the cancelled hop must not write left/top
  const { fx, anims, writes } = hopRig();
  const done = fx.hop(0, P1, 1, 0, false);
  const a = anims[0];
  fx.snap();
  a.complete(); // a late "finish" after cancel must be ignored too
  assert.equal(await done, false);
  await tick();
  assert.deepEqual(writes, [], 'snap: no inline position written');
  fx.dispose();
}
{ // superseded hop: only the newer hop places the pawn
  const { fx, anims, writes } = hopRig();
  const first = fx.hop(0, P1, 1, 0, false);
  const second = fx.hop(0, P2, 2, 0, false);
  assert.equal(await first, false);
  await tick();
  assert.deepEqual(writes, [], 'superseded hop wrote nothing');
  anims[anims.length - 1].complete();
  assert.equal(await second, true);
  assert.deepEqual(writes, [['left', '30%'], ['top', '90%']]);
  fx.dispose();
}
{ // cancelHop (burst path): in-flight hop cancelled, no write
  const { fx, anims, writes } = hopRig();
  const done = fx.hop(0, P1, 1, 0, false);
  fx.cancelHop(0);
  assert.equal(await done, false);
  await tick();
  assert.deepEqual(writes, []);
  assert.equal(anims.length, 0);
  fx.dispose();
}
console.log('board2d-fx tests ok');
