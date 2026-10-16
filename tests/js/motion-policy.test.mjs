import assert from 'node:assert/strict';
import { REDUCE_QUERY, osReducedMotion, effectiveReduce, playsFx, cameraCue } from '../../src/Game.Web/wwwroot/js/3d/policy.js';

// OS preference through an injected matchMedia
let asked = null;
assert.equal(osReducedMotion(q => { asked = q; return { matches: true }; }), true);
assert.equal(asked, REDUCE_QUERY);
assert.equal(osReducedMotion(() => ({ matches: false })), false);
assert.equal(osReducedMotion(undefined), false, 'no matchMedia');
assert.equal(osReducedMotion(() => { throw new Error('x'); }), false, 'matchMedia throws');

// in-app OR OS
assert.equal(effectiveReduce(false, false), false);
assert.equal(effectiveReduce(true, false), true);
assert.equal(effectiveReduce(false, true), true, 'OS preference alone reduces motion');

// effects only for live, animated events
assert.equal(playsFx(false, false), true);
assert.equal(playsFx(true, false), false, 'burst');
assert.equal(playsFx(false, true), false, 'reduced motion');

// camera cues
assert.equal(cameraCue('TurnStarted', false), 'follow');
assert.equal(cameraCue('AuctionStarted', false), 'overview');
assert.equal(cameraCue('TradeProposed', false), 'overview');
assert.equal(cameraCue('PlayerMoved', false), null);
assert.equal(cameraCue(undefined, false), null);

console.log('motion-policy tests ok');
