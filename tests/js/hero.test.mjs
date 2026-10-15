import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { heroMode, autoTier, NARROW_PX } from '../../src/Game.Web/wwwroot/js/hero-policy.js';
import { createDemo } from '../../src/Game.Web/wwwroot/js/hero3d.js';

// --- start/stop policy ---
const desk = { quality: 'auto', reduceMotion: false, prefersReduced: false, width: 1280, cores: 8, webgl: true };
assert.equal(heroMode(desk), 'gl', 'desktop auto -> gl');
assert.equal(heroMode({ ...desk, quality: 'high' }), 'gl');
assert.equal(heroMode({ ...desk, quality: 'low' }), 'css', 'quality low -> css');
assert.equal(heroMode({ ...desk, width: NARROW_PX }), 'css', 'narrow auto -> css');
assert.equal(heroMode({ ...desk, width: 390 }), 'css');
assert.equal(heroMode({ ...desk, cores: 4 }), 'css', 'low-core auto -> css');
assert.equal(heroMode({ ...desk, width: 390, quality: 'high' }), 'gl', 'explicit high wins over device size');
assert.equal(heroMode({ ...desk, webgl: false }), 'css', 'no webgl -> css');
assert.equal(heroMode({ ...desk, webgl: false, quality: 'high' }), 'css');
assert.equal(heroMode({ ...desk, reduceMotion: true }), 'still', 'in-app reduced motion -> still');
assert.equal(heroMode({ ...desk, prefersReduced: true, quality: 'high' }), 'still', 'OS reduced motion -> still');
assert.equal(heroMode({ ...desk, prefersReduced: true, webgl: false }), 'still');
assert.equal(autoTier({ width: 701, cores: 8 }), 'high');
assert.equal(autoTier({ width: 700, cores: 8 }), 'low');
assert.equal(autoTier({ width: 1920, cores: 4 }), 'low');
assert.equal(autoTier({ width: 1920, cores: 0 }), 'high', 'unknown cores counts as 8');

// --- scripted demo game ---
const board = JSON.parse(readFileSync(new URL('../../boards/neo_city.json', import.meta.url)));
const demo = createDemo(board);
const n = board.Tiles.length;
let s0 = demo.state();
assert.equal(s0.Players.length, 4);
assert.equal(s0.Properties.length, n);
s0.Properties.forEach((p, i) => assert.equal(!!p, !!board.Tiles[i].IsOwnable, 'property slot only for ownable tiles'));
for (let t = 0; t < 200; t++) {
  const before = demo.state();
  const steps = demo.turn();
  const roll = steps[0][1][0], move = steps[1][1][0];
  assert.equal(roll.$t, 'DiceRolled');
  assert.ok(roll.D1 >= 1 && roll.D1 <= 6 && roll.D2 >= 1 && roll.D2 <= 6);
  assert.equal(move.$t, 'PlayerMoved');
  assert.equal(move.From, before.Players[move.Player].Position);
  assert.equal(move.To, (move.From + roll.D1 + roll.D2) % n);
  for (let i = 1; i < steps.length; i++) assert.ok(steps[i][0] >= steps[i - 1][0], 'steps in time order');
  const after = demo.state();
  for (const p of after.Properties) if (p) { assert.ok(p.Level >= 0 && p.Level <= 5); assert.ok(p.Owner >= -1 && p.Owner < 4); }
}
// same seed, same game
const a = createDemo(board, 3), b = createDemo(board, 3);
for (let t = 0; t < 20; t++) assert.deepEqual(a.turn(), b.turn());
console.log('hero tests ok');
