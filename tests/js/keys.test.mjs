import assert from 'node:assert/strict';
import { classify, attach } from '../../src/Game.Web/wwwroot/js/keys.js';

const el = (tagName, extra = {}) => ({ nodeType: 1, tagName, getAttribute: k => extra[k] ?? null, closest: () => null, ...extra });
const body = el('BODY');
const k = (key, mods = {}) => ({ key, ...mods });

assert.equal(classify(k(' '), body, false), 'roll');
assert.equal(classify(k('b'), body, false), 'buy');
assert.equal(classify(k('B'), body, false), 'buy', 'caps lock');
assert.equal(classify(k('e'), body, false), 'end');
assert.equal(classify(k('m'), body, false), 'mute');
assert.equal(classify(k('l'), body, false), 'log');
assert.equal(classify(k('Escape'), body, false), 'escape');
assert.equal(classify(k('x'), body, false), null, 'unmapped');

for (const tag of ['INPUT', 'SELECT', 'TEXTAREA']) {
  assert.equal(classify(k('b'), el(tag), false), null, tag + ' ignores shortcuts');
  assert.equal(classify(k(' '), el(tag), false), null, tag + ' ignores space');
}
assert.equal(classify(k('e'), el('DIV', { isContentEditable: true }), false), null, 'contenteditable');
assert.equal(classify(k('Escape'), el('INPUT'), false), 'escape', 'escape closes from inputs');

for (const mod of ['ctrlKey', 'metaKey', 'altKey', 'shiftKey'])
  assert.equal(classify(k('b', { [mod]: true }), body, false), null, mod);
assert.equal(classify(k('b', { repeat: true }), body, false), null, 'auto-repeat');

assert.equal(classify(k('b'), body, true), null, 'modal open blocks game keys');
assert.equal(classify(k('Escape'), body, true), 'escape', 'modal open allows escape');
assert.equal(classify(k(' '), el('BUTTON'), false), null, 'space presses the focused button');
assert.equal(classify(k('b'), el('BUTTON'), false), 'buy', 'letters still work on a focused button');

// attach/dispose: listener added and removed, invokes .NET with the action
const listeners = new Map();
globalThis.document = {
  activeElement: body,
  querySelector: () => null,
  addEventListener: (t, f) => listeners.set(t, f),
  removeEventListener: (t, f) => { if (listeners.get(t) === f) listeners.delete(t); },
};
const calls = [];
const h = attach({ invokeMethodAsync: (m, a) => { calls.push([m, a]); return Promise.resolve(); } });
let prevented = false;
listeners.get('keydown')({ key: ' ', preventDefault: () => { prevented = true; } });
assert.deepEqual(calls, [['OnShortcut', 'roll']]);
assert.ok(prevented, 'space does not scroll the page');
h.dispose();
assert.equal(listeners.size, 0, 'listener removed on dispose');
console.log('keys tests ok');
