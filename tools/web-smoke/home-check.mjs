// Home and Lobby check for the web client in real Chromium.
//   * Home at 1280x800 and 390x844 (phone emulation) in every theme: screenshots, no horizontal overflow,
//     touch targets >= 44 px, the hero in the expected mode (desktop Auto: WebGL; phone Auto: CSS fallback).
//   * Theme chips apply live (data-theme on <html>) and persist (localStorage); the sound toggle persists.
//   * Keyboard: the first Tab stops land on visible, focus-ringed controls.
//   * Hero policy: prefers-reduced-motion -> still CSS hero; Quality Low -> CSS hero; neither creates a WebGL context.
//   * The hero never intercepts clicks (elementFromPoint over the hero hits page content or the body).
//   * Lifecycle, 3 times: Home -> Create room -> Lobby (Add bot, Ready, Start match) -> Play -> Home. Asserts no
//     console errors, the hero counters (live renderers / running loops) back at 1 on Home and 0 on Play, live
//     WebGL contexts not growing, and the requestAnimationFrame rate on Home not growing between rounds.
//   * Lobby: screenshots at both sizes, the copy-code button shows its confirmation.
//
//   node tools/web-smoke/home-check.mjs [outDir] [--base=http://127.0.0.1:5198/] [--serve=<wwwroot> --api=<port>]
//        [--gl=egl|swiftshader] [--themes=neon-noir,classic-gold,frost,sakura] [--rounds=3]
//
// The app must be reachable at --base with /api and /ws routed to a Game.Server, or pass --serve and --api so this
// script serves the published wwwroot and proxies /api and WebSockets itself (see mobile-check.mjs for the
// server command). WebGL uses --gl=egl by default on arm64 (SwiftShader crashes there). Exit code 1 on any failure.
import http from 'node:http';
import net from 'node:net';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const args = process.argv.slice(2);
const opt = k => (args.find(a => a.startsWith(`--${k}=`)) || '').split('=').slice(1).join('=') || null;
const outDir = path.resolve(args.find(a => !a.startsWith('--')) || path.join(repo, 'tools/web-smoke/home-out'));
const base = opt('base') || 'http://127.0.0.1:5198/';
const themes = (opt('themes') || 'neon-noir,classic-gold,frost,sakura').split(',');
const rounds = +(opt('rounds') || 3);
fs.mkdirSync(outDir, { recursive: true });

// --- optional built-in static server + API/WS proxy ---
let server = null;
if (opt('serve')) {
  const root = path.resolve(opt('serve')), api = +(opt('api') || 5199), port = +new URL(base).port;
  const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
    '.wasm': 'application/wasm', '.png': 'image/png', '.svg': 'image/svg+xml', '.webmanifest': 'application/manifest+json' };
  server = http.createServer((req, res) => {
    if (req.url.startsWith('/api') || req.url.startsWith('/healthz')) {
      const p = http.request({ host: '127.0.0.1', port: api, path: req.url, method: req.method, headers: req.headers }, r => { res.writeHead(r.statusCode, r.headers); r.pipe(res); });
      p.on('error', () => { res.writeHead(502); res.end(); });
      req.pipe(p); return;
    }
    let f = path.join(root, decodeURIComponent(req.url.split('?')[0]));
    if (!f.startsWith(root)) { res.writeHead(403); return res.end(); }
    if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) f = path.join(root, 'index.html');
    res.writeHead(200, { 'content-type': types[path.extname(f)] || 'application/octet-stream' });
    fs.createReadStream(f).pipe(res);
  });
  server.on('upgrade', (req, sock, head) => {
    const up = net.connect(api, '127.0.0.1', () => {
      up.write(`${req.method} ${req.url} HTTP/1.1\r\n` + Object.entries(req.headers).map(([k, v]) => `${k}: ${v}`).join('\r\n') + '\r\n\r\n');
      up.write(head); sock.pipe(up); up.pipe(sock);
    });
    up.on('error', () => sock.destroy()); sock.on('error', () => up.destroy());
  });
  await new Promise(r => server.listen(port, '127.0.0.1', r));
}

const GL = (opt('gl') || (process.arch === 'arm64' ? 'egl' : 'swiftshader')) === 'egl' ? ['--use-gl=angle', '--use-angle=gl-egl']
  : ['--use-gl=angle', '--use-angle=swiftshader-webgl', '--enable-unsafe-swiftshader'];
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined,
  args: [...GL, '--no-sandbox', '--disable-gpu-sandbox', '--ignore-gpu-blocklist'] });

const results = [];
const check = (run, name, ok, detail = '') => { results.push({ run, name, ok: !!ok, detail }); return ok; };
const DESKTOP = { viewport: { width: 1280, height: 800 } };
const PHONE = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 3, isMobile: true, hasTouch: true,
  userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36' };

// Counts WebGL contexts (created / lost) and requestAnimationFrame calls, before any app script runs.
function instrument() {
  const gl = { created: 0, lost: 0 };
  const raf = { n: 0 };
  window.__gl = gl; window.__raf = raf;
  const get = HTMLCanvasElement.prototype.getContext;
  const seen = new WeakSet();
  HTMLCanvasElement.prototype.getContext = function (type, ...rest) {
    const ctx = get.call(this, type, ...rest);
    if (ctx && /webgl/.test(type) && !seen.has(ctx)) {
      seen.add(ctx); gl.created++;
      this.addEventListener('webglcontextlost', () => gl.lost++);
      const lose = ctx.getExtension('WEBGL_lose_context');
      if (lose) {   // a probe that loses its context at once may never dispatch the event before the page checks
        const orig = lose.loseContext.bind(lose);
        let done = false;
        lose.loseContext = () => { if (!done) { done = true; gl.lost++; this.__counted = true; } orig(); };
        this.addEventListener('webglcontextlost', () => { if (this.__counted) gl.lost--; this.__counted = false; }, { once: true });
      }
    }
    return ctx;
  };
  const r = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = cb => { raf.n++; return r(cb); };
}

function settings(theme, extra = {}) {
  return JSON.stringify({ Theme: theme, Quality: 'auto', Master: 1, Sfx: 1, Music: 0.5, Muted: true, ReduceMotion: false, View3d: true, ...extra });
}

// cores: what navigator.hardwareConcurrency reports. Desktop runs claim 8 so Auto behaves as on a real desktop
// (this CI host has 2 cores, which Auto rightly treats as low-end).
async function open(ctxOpts, initSettings, run, cores = ctxOpts.isMobile ? 0 : 8) {
  const ctx = await browser.newContext(ctxOpts);
  await ctx.addInitScript(instrument);
  if (cores) await ctx.addInitScript(n => Object.defineProperty(Navigator.prototype, 'hardwareConcurrency', { get: () => n, configurable: true }), cores);
  await ctx.addInitScript(s => {
    try { if (!sessionStorage.getItem('hc-init')) { sessionStorage.setItem('hc-init', '1'); localStorage.setItem('boardempire.settings', s); } } catch { }
  }, initSettings);
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errors.push(m.type() + ': ' + m.text().slice(0, 300)); });
  const shot = async name => { try { await page.screenshot({ path: path.join(outDir, `${run}-${name}.png`), timeout: 60000 }); } catch (e) { console.log('shot failed', run, name, e.message.split('\n')[0]); } };
  return { ctx, page, errors, shot };
}

const heroMode = page => page.evaluate(() => {
  const el = document.querySelector('.herobg');
  return el ? [...el.classList].find(c => c.startsWith('mode-'))?.slice(5) || '?' : 'none';
});
async function waitHero(page, want, ms = 20000) {
  const end = Date.now() + ms;
  let m;
  while (Date.now() < end) {
    m = await heroMode(page);
    if (m === want && (want !== 'gl' || await page.locator('canvas.herobg-gl.ready').count())) return m;
    await page.waitForTimeout(200);
  }
  return m;
}
const heroStats = page => page.evaluate(async () => (await import('/js/hero3d.js')).__test.stats());
const overflow = async (page, run, label) => {
  const o = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: innerWidth }));
  check(run, `no horizontal overflow (${label})`, o.sw <= o.iw, JSON.stringify(o));
};
const targets = async (page, run, scope, label) => {
  const small = await page.evaluate(scope => {
    const out = [];
    for (const el of document.querySelectorAll(`${scope} button, ${scope} input, ${scope} select, ${scope} a[href]`)) {
      const cs = getComputedStyle(el);
      if (cs.display === 'none' || cs.visibility === 'hidden') continue;
      const r = el.getBoundingClientRect();
      if (r.width === 0 && r.height === 0) continue;
      if (r.width < 43.5 || r.height < 43.5) out.push(`${(el.getAttribute('aria-label') || el.textContent || el.id || el.tagName).trim().slice(0, 24)} ${Math.round(r.width)}x${Math.round(r.height)}`);
    }
    return out;
  }, scope);
  check(run, `touch targets >= 44px (${label})`, small.length === 0, small.join('; '));
};
const homeReady = async page => {
  await page.waitForSelector('.wordmark', { timeout: 60000 });
  await page.waitForFunction(() => !document.querySelector('.rooms-body[aria-busy="true"]'), null, { timeout: 30000 });
  await page.waitForTimeout(1300);   // entrance stagger
};

// ---------- 1. Home in every theme at desktop and phone size ----------
for (const [size, dev, wantHero] of [['desktop', DESKTOP, 'gl'], ['phone', PHONE, 'css']]) {
  for (const theme of themes) {
    const run = `home-${size}-${theme}`;
    const { ctx, page, errors, shot } = await open(dev, settings(theme), run);
    try {
      await page.goto(base, { waitUntil: 'networkidle', timeout: 60000 });
      await homeReady(page);
      const m = await waitHero(page, wantHero);
      check(run, `hero mode ${wantHero}`, m === wantHero, m);
      check(run, 'theme applied', await page.evaluate(() => document.documentElement.dataset.theme) === theme);
      if (wantHero === 'gl') await page.waitForTimeout(2500);   // let the demo roll and hop once
      await overflow(page, run, 'home');
      await targets(page, run, 'main.home', 'home');
      await shot('home');
      if (size === 'phone') { await page.evaluate(() => scrollTo(0, document.body.scrollHeight)); await page.waitForTimeout(400); await shot('home-bottom'); }
      // the hero never takes clicks
      const hit = await page.evaluate(() => { const e = document.elementFromPoint(innerWidth * 0.3, innerHeight * 0.75); return e ? !e.closest('.herobg') : true; });
      check(run, 'hero does not intercept pointer', hit);
    } catch (e) { check(run, 'flow', false, e.message.split('\n')[0]); }
    check(run, 'no console errors', errors.length === 0, errors.join(' | '));
    await ctx.close();
  }
}

// ---------- 2. theme chips, sound toggle, keyboard focus ----------
{
  const run = 'home-controls';
  const { ctx, page, errors, shot } = await open(DESKTOP, settings('neon-noir'), run);
  try {
    await page.goto(base, { waitUntil: 'networkidle', timeout: 60000 });
    await homeReady(page);
    await page.getByRole('radio', { name: 'Sakura' }).click();
    await page.waitForFunction(() => document.documentElement.dataset.theme === 'sakura', null, { timeout: 5000 }).catch(() => {});
    const t = await page.evaluate(() => ({ attr: document.documentElement.dataset.theme, stored: JSON.parse(localStorage.getItem('boardempire.settings') || '{}').Theme,
      checked: document.querySelector('.theme-chips [aria-checked="true"]')?.textContent.trim() }));
    check(run, 'theme chip applies live and persists', t.attr === 'sakura' && t.stored === 'sakura' && t.checked === 'Sakura', JSON.stringify(t));
    await page.waitForTimeout(800);
    await shot('sakura-live');
    const snd = page.getByRole('button', { name: 'Sound' });
    const before = await snd.getAttribute('aria-pressed');
    await snd.click();
    await page.waitForTimeout(300);
    const after = await snd.getAttribute('aria-pressed');
    const muted = await page.evaluate(() => JSON.parse(localStorage.getItem('boardempire.settings') || '{}').Muted);
    check(run, 'sound toggle flips and persists', before === 'false' && after === 'true' && muted === false, `${before}->${after} muted=${muted}`);
    // reload: theme and sound survive
    await page.reload({ waitUntil: 'networkidle' });
    await homeReady(page);
    const kept = await page.evaluate(() => ({ theme: document.documentElement.dataset.theme, sound: document.querySelector('[aria-label="Sound"]')?.getAttribute('aria-pressed') }));
    check(run, 'theme and sound survive reload', kept.theme === 'sakura' && kept.sound === 'true', JSON.stringify(kept));
    // keyboard: tab through the page, every stop visible and outlined
    await page.mouse.click(5, 5);
    const stops = [];
    for (let i = 0; i < 16; i++) {
      await page.keyboard.press('Tab');
      const st = await page.evaluate(() => {
        const e = document.activeElement;
        if (!e || e === document.body) return null;
        const r = e.getBoundingClientRect(), cs = getComputedStyle(e);
        return { name: (e.getAttribute('aria-label') || e.textContent || e.placeholder || e.tagName).trim().slice(0, 20), w: r.width, h: r.height, ring: cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) >= 2 };
      });
      if (!st) break;
      stops.push(st);
    }
    check(run, 'at least 14 tab stops before leaving the page', stops.length >= 14, stops.map(s => s.name).join(' > '));
    const bad = stops.filter(s => !s.ring || s.w < 1);
    check(run, 'tab stops visible with focus ring', bad.length === 0, bad.map(s => s.name).join('; ') || stops.map(s => s.name).join(' > '));
    await page.keyboard.press('Shift+Tab');
    await shot('focus');
    // name validation hint
    await page.fill('#player-name', 'A very long player name');
    const hint = await page.textContent('#name-hint');
    check(run, 'name hint warns about the 16-char limit', /16/.test(hint || ''), hint);
    await page.fill('#join-code', 'ab-1');
    const jh = await page.textContent('#join-hint');
    check(run, 'room code hint', /letters and numbers/.test(jh || ''), jh);
    // settings sheet opens from the gear and closes with Escape
    await page.getByRole('button', { name: 'Settings' }).click();
    await page.waitForSelector('[role=dialog]', { timeout: 5000 });
    await page.waitForTimeout(500);
    await shot('settings');
    await page.keyboard.press('Escape');
    await page.waitForTimeout(400);
    check(run, 'settings sheet closes with Escape', await page.locator('[role=dialog]').count() === 0);
  } catch (e) { check(run, 'flow', false, e.message.split('\n')[0]); }
  check(run, 'no console errors', errors.length === 0, errors.join(' | '));
  await ctx.close();
}

// ---------- 3. hero policy: reduced motion and Low quality use the CSS hero, no GL ----------
for (const [run, ctxOpts, s, want] of [
  ['hero-reduced-motion', { ...DESKTOP, reducedMotion: 'reduce' }, settings('neon-noir'), 'still'],
  ['hero-quality-low', DESKTOP, settings('frost', { Quality: 'low' }), 'css'],
  ['hero-phone-high', PHONE, settings('classic-gold', { Quality: 'high' }), 'gl'],
]) {
  const { ctx, page, errors, shot } = await open(ctxOpts, s, run);
  try {
    await page.goto(base, { waitUntil: 'networkidle', timeout: 60000 });
    await homeReady(page);
    const m = await waitHero(page, want, 8000);
    check(run, `hero mode ${want}`, m === want, m);
    if (want !== 'gl') {
      await page.waitForTimeout(800);
      const g = await page.evaluate(() => ({ ...window.__gl, canvas: document.querySelectorAll('.herobg canvas').length }));
      check(run, 'no WebGL context on Home', g.canvas === 0 && g.created === 0, JSON.stringify(g));
      if (want === 'still') {
        const anim = await page.evaluate(() => document.getAnimations().filter(a => a.playState === 'running').length);
        check(run, 'nothing animates under reduced motion', anim === 0, `${anim} running animations`);
      }
    } else await page.waitForTimeout(2000);
    await shot('home');
  } catch (e) { check(run, 'flow', false, e.message.split('\n')[0]); }
  check(run, 'no console errors', errors.length === 0, errors.join(' | '));
  await ctx.close();
}

// ---------- 4. lifecycle: Home -> Lobby -> Play -> Home, repeatedly ----------
{
  const run = 'lifecycle';
  const { ctx, page, errors, shot } = await open(DESKTOP, settings('neon-noir'), run);
  await ctx.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: new URL(base).origin });
  const rates = [], lives = [];
  try {
    await page.goto(base, { waitUntil: 'networkidle', timeout: 60000 });
    for (let i = 1; i <= rounds; i++) {
      await homeReady(page);
      check(run, `round ${i}: hero gl on Home`, await waitHero(page, 'gl') === 'gl');
      await page.waitForTimeout(1500);
      const s = await heroStats(page);
      check(run, `round ${i}: one hero renderer and loop on Home`, s.instances === 1 && s.loops === 1, JSON.stringify(s));
      const r0 = await page.evaluate(() => window.__raf.n);
      await page.waitForTimeout(2000);
      const rate = (await page.evaluate(() => window.__raf.n) - r0) / 2;
      rates.push(rate);
      const g = await page.evaluate(() => window.__gl);
      lives.push(g.created - g.lost);
      if (i === 1) await page.fill('#player-name', 'Ishan');
      await page.getByRole('button', { name: 'Create room' }).click();
      await page.waitForURL(/\/room\//, { timeout: 60000 });
      await page.waitForSelector('.seats .seat', { timeout: 30000 });
      await page.waitForTimeout(400);
      const s2 = await heroStats(page);
      check(run, `round ${i}: hero disposed in Lobby`, s2.instances === 0 && s2.loops === 0, JSON.stringify(s2));
      await page.locator('.seat.empty').getByRole('button', { name: 'Add bot' }).click();
      await page.waitForFunction(() => document.querySelectorAll('.seats .seat.bot').length >= 1, null, { timeout: 15000 });
      await page.getByRole('button', { name: 'Ready', exact: true }).click();
      await page.waitForFunction(() => document.querySelector('.ready-btn')?.getAttribute('aria-pressed') === 'true', null, { timeout: 10000 });
      if (i === 1) {
        await page.waitForTimeout(900);
        await shot('lobby-desktop');
        await page.getByRole('button', { name: 'Copy room code' }).click();
        await page.waitForTimeout(500);
        const copied = { done: await page.locator('.copy.done').count(), toast: (await page.locator('.toast').allTextContents()).join('|'),
          clip: await page.evaluate(() => navigator.clipboard.readText()).catch(e => 'ERR ' + e.message) };
        check(run, 'copy puts the code on the clipboard with button + toast feedback', copied.done === 1 && /copied/.test(copied.toast) && page.url().endsWith(copied.clip), JSON.stringify(copied));
        await shot('lobby-copied');
      }
      await page.getByRole('button', { name: 'Start match' }).click();
      await page.waitForURL(/\/play\//, { timeout: 60000 });
      await page.waitForSelector('canvas.board3d, .board2d', { timeout: 60000 });
      await page.waitForTimeout(2500);
      const s3 = await heroStats(page);
      check(run, `round ${i}: no hero on Play`, s3.instances === 0 && s3.loops === 0, JSON.stringify(s3));
      if (i === 1) await shot('play');
      await page.evaluate(() => window.Blazor.navigateTo('/'));
      await page.waitForURL(u => new URL(u).pathname === '/', { timeout: 30000 });
    }
    await homeReady(page);
    await waitHero(page, 'gl');
    await page.waitForTimeout(1500);
    const sEnd = await heroStats(page);
    const r0 = await page.evaluate(() => window.__raf.n);
    await page.waitForTimeout(2000);
    rates.push((await page.evaluate(() => window.__raf.n) - r0) / 2);
    const g = await page.evaluate(() => window.__gl);
    lives.push(g.created - g.lost);
    check(run, 'final Home: exactly one hero renderer and loop', sEnd.instances === 1 && sEnd.loops === 1, JSON.stringify(sEnd));
    check(run, 'rAF rate on Home does not grow', rates[rates.length - 1] <= rates[0] * 1.35 + 5, rates.map(r => r.toFixed(0)).join(' -> ') + ' calls/s');
    check(run, 'live WebGL contexts do not grow', lives[lives.length - 1] <= lives[0], lives.join(' -> ') + ` (created ${g.created}, lost ${g.lost})`);
    await shot('home-after');
  } catch (e) { check(run, 'flow', false, e.message.split('\n')[0]); await shot('fail'); }
  check(run, 'no console errors', errors.length === 0, errors.join(' | '));
  await ctx.close();
}

// ---------- 5. Lobby on a phone ----------
{
  const run = 'lobby-phone';
  const { ctx, page, errors, shot } = await open(PHONE, settings('frost'), run);
  try {
    await page.goto(base, { waitUntil: 'networkidle', timeout: 60000 });
    await homeReady(page);
    await page.fill('#player-name', 'Phone');
    await page.getByRole('button', { name: 'Create room' }).tap();
    await page.waitForURL(/\/room\//, { timeout: 60000 });
    await page.waitForSelector('.seats .seat', { timeout: 30000 });
    await page.locator('.seat.empty').getByRole('button', { name: 'Add bot' }).tap();
    await page.waitForFunction(() => document.querySelectorAll('.seats .seat.bot').length >= 1, null, { timeout: 15000 });
    await page.waitForTimeout(1200);
    await page.getByRole('button', { name: 'Copy room code' }).tap();
    await page.waitForTimeout(800);
    const t = (await page.locator('.toast').allTextContents()).join('|');
    check(run, 'copy gives toast feedback (with or without clipboard permission)', /copied|Couldn't copy/.test(t), t);
    await overflow(page, run, 'lobby');
    await targets(page, run, 'main.lobby', 'lobby');
    await shot('lobby');
    await page.evaluate(() => scrollTo(0, document.body.scrollHeight));
    await page.waitForTimeout(400);
    await shot('lobby-bottom');
  } catch (e) { check(run, 'flow', false, e.message.split('\n')[0]); }
  check(run, 'no console errors', errors.length === 0, errors.join(' | '));
  await ctx.close();
}

await browser.close();
if (server) server.close();
let failed = 0;
for (const r of results) {
  if (!r.ok) failed++;
  console.log(`${r.ok ? 'ok  ' : 'FAIL'} [${r.run}] ${r.name}${r.detail && (!r.ok || process.env.VERBOSE) ? ' — ' + r.detail : ''}`);
}
console.log(`${results.length - failed}/${results.length} checks passed; screenshots in ${outDir}`);
process.exit(failed ? 1 : 0);
