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
  return probeQuality(() => {}, 20, () => (t += ms));
}
assert.equal(await probe(16), 'high');
assert.equal(await probe(28), 'high');
assert.equal(await probe(29), 'low');
assert.equal(await probeQuality(() => { throw new Error('x'); }, 5), 'low');
console.log('fx tests ok');
