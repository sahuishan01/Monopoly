import assert from 'node:assert/strict';
import { ParticlePool, probeQuality, capFor } from '../../src/Game.Web/wwwroot/js/fx.js';

assert.equal(capFor('high'), 400); assert.equal(capFor('low'), 80);
const pool = new ParticlePool(400, 80);
for (let i = 0; i < 1000; i++) pool.spawn(0, 1, 0, 0, 1, 0, 1, 1, 1, 5);
assert.equal(pool.alive(), 80, 'low cap');
pool.setCap(400);
for (let i = 0; i < 1000; i++) pool.spawn(0, 1, 0, 0, 1, 0, 1, 1, 1, 5);
assert.equal(pool.alive(), 400, 'high cap');
pool.setCap(80); assert.equal(pool.alive(), 80, 'shrink kills');
pool.step(10); assert.equal(pool.alive(), 0, 'expire');

async function probe(ms) {
  let t = 0;
  return probeQuality(() => {}, 20, () => (t += ms), () => false);
}
async function probeSeq(steps, hiddenAt = []) {
  let i = 0, t = 0;
  return probeQuality(() => {}, 20, () => (t += steps[Math.min(i++, steps.length - 1)]), () => hiddenAt.includes(i));
}
assert.equal(await probe(16), 'high');
assert.equal(await probe(28), 'high');
assert.equal(await probe(29), 'low');
assert.equal(await probe(40), 'low', 'steady 40ms');
assert.equal(await probe(16), 'high', 'steady 16ms');
// a 5 s tab-resume gap and an isolated hitch must not downgrade
assert.equal(await probeSeq([16, 16, 16, 5000, 16, 16, 90, ...Array(40).fill(16)]), 'high', 'gap/hitch');
// hidden period restarts sampling; slow frames while hidden are discarded
assert.equal(await probeSeq([...Array(10).fill(100), ...Array(60).fill(16)], [3, 4, 5, 6, 7, 8, 9, 10]), 'high', 'hidden');
assert.equal(await probeQuality(() => { throw new Error('x'); }, 5), 'low');
console.log('fx tests ok');
