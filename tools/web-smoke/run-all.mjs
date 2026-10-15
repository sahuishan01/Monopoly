// One entry point for every web check. Starts its own stack (harness.mjs: publish, Game.Server on a free loopback
// port, static server + proxy on another), runs the checks one after another, stops the stack and prints a summary.
//
//   node tools/web-smoke/run-all.mjs [--out=<dir>] [--serve=<published wwwroot>] [--only=a,b] [--skip=a,b] [--gl=egl|swiftshader]
//
// Checks: unit (tests/js/*.test.mjs), render (render-check), smoke, e2e (e2e-check), home (home-check),
//         mobile (mobile-check). Screenshots go to <out>/<check>. Exit code 1 if any check failed.
// Needs: dotnet 8 SDK, `npm ci` in tools/web-smoke, and CHROMIUM_PATH when the playwright-bundled browser is
// not installed. See README.md.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { startStack, repo } from './harness.mjs';

const args = process.argv.slice(2);
const opt = k => (args.find(a => a.startsWith(`--${k}=`)) || '').split('=').slice(1).join('=') || null;
const out = path.resolve(opt('out') || fs.mkdtempSync(path.join(os.tmpdir(), 'be-web-checks-')));
const only = opt('only') ? opt('only').split(',') : null;
const skip = (opt('skip') || '').split(',').filter(Boolean);
const gl = opt('gl') ? [`--gl=${opt('gl')}`] : [];
const want = n => (!only || only.includes(n)) && !skip.includes(n);
fs.mkdirSync(out, { recursive: true });

function sh(label, cmd, argv) {
  return new Promise(res => {
    console.log(`\n===== ${label}: ${cmd} ${argv.join(' ')}`);
    const t0 = Date.now();
    const p = spawn(cmd, argv, { cwd: repo, stdio: 'inherit', env: process.env });
    p.on('exit', code => { console.log(`===== ${label}: exit ${code} in ${((Date.now() - t0) / 1000).toFixed(0)} s`); res(code ?? 1); });
  });
}

const summary = [];
const W = path.join(repo, 'tools/web-smoke');
if (want('unit')) {
  const files = fs.readdirSync(path.join(repo, 'tests/js')).filter(f => f.endsWith('.test.mjs')).sort();
  let code = 0;
  for (const f of files) code |= await sh('unit ' + f, 'node', [path.join('tests/js', f)]);
  summary.push(['unit', code]);
}
if (want('render')) summary.push(['render', await sh('render', 'node', [path.join(W, 'render-check.mjs'), path.join(out, 'render'), ...gl])]);

const needStack = ['smoke', 'e2e', 'home', 'mobile'].some(want);
let stack = null;
if (needStack) {
  try { stack = await startStack({ wwwroot: opt('serve') || undefined, log: path.join(out, 'server.log') }); }
  catch (e) { console.error('stack failed to start:', e.message); summary.push(['stack', 1]); }
}
const stop = async () => { if (stack) { const s = stack; stack = null; await s.stop(); } };
for (const [sig, code] of [['SIGINT', 130], ['SIGTERM', 143]]) process.on(sig, async () => { await stop(); process.exit(code); });
try {
  if (stack) {
    const b = `--base=${stack.base}`;
    if (want('smoke')) summary.push(['smoke', await sh('smoke', 'node', [path.join(W, 'smoke.mjs'), stack.base, ...gl])]);
    if (want('e2e')) summary.push(['e2e', await sh('e2e', 'node', [path.join(W, 'e2e-check.mjs'), path.join(out, 'e2e'), b, ...gl])]);
    if (want('home')) summary.push(['home', await sh('home', 'node', [path.join(W, 'home-check.mjs'), path.join(out, 'home'), b, ...gl])]);
    if (want('mobile')) summary.push(['mobile', await sh('mobile', 'node', [path.join(W, 'mobile-check.mjs'), path.join(out, 'mobile'), b, ...gl])]);
  }
} finally { await stop(); }

console.log('\n===== summary');
for (const [n, c] of summary) console.log(`${c === 0 ? 'ok  ' : 'FAIL'} ${n}${c ? ` (exit ${c})` : ''}`);
console.log('output in', out);
process.exit(summary.some(([, c]) => c !== 0) ? 1 : 0);
