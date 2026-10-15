// Full-journey check of the web client in real Chromium (desktop, then a phone viewport):
//   * a theme chip on Home applies data-theme on <html> and persists;
//   * Play vs bots reaches Play; the 3D canvas renders a non-uniform image (pixel sample of the canvas);
//   * tapping/clicking a tile opens the property card, in 3D (picked through the board3d test hooks) and in 2D;
//   * the 2D / 3D toggle swaps the board both ways;
//   * the settings sheet switches theme during Play and every surface takes the new colours at once
//     (no colour transition left stuck on the old theme), sets Quality and Mute, and all three survive a reload;
//   * the M shortcut toggles mute;
//   * phone viewport (touch, DPR 3): theme switch, 2D tile tap -> card, no horizontal overflow, 3D renders;
//   * no console errors or warnings outside a short allow-list of known-benign software-GL messages.
//
//   node tools/web-smoke/e2e-check.mjs [outDir] --base=http://127.0.0.1:<port>/ [--gl=egl|swiftshader]
//   (or run tools/web-smoke/run-all.mjs, which starts the stack itself)
// Set CHROMIUM_PATH to use a browser other than the one bundled with this playwright version. Exit code 1 on failure.
import fs from 'node:fs';
import path from 'node:path';
import { chromium } from 'playwright';
import { browserArgs, repo } from './harness.mjs';

const args = process.argv.slice(2);
const opt = k => (args.find(a => a.startsWith(`--${k}=`)) || '').split('=').slice(1).join('=') || null;
const outDir = path.resolve(args.find(a => !a.startsWith('--')) || path.join(repo, 'tools/web-smoke/e2e-out'));
const base = opt('base') || 'http://127.0.0.1:5198/';
fs.mkdirSync(outDir, { recursive: true });

// Messages Chromium prints under software GL that say nothing about the app.
const BENIGN = [/GPU stall due to ReadPixels/i, /Automatic fallback to software WebGL/i, /GroupMarkerNotSet/i];

const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined, args: browserArgs(opt('gl') || undefined) });
const results = [];
const check = (run, name, ok, detail = '') => { results.push({ run, name, ok: !!ok, detail: typeof detail === 'string' ? detail : JSON.stringify(detail) }); return ok; };
const SETTINGS = (extra = {}) => JSON.stringify({ Theme: 'neon-noir', Quality: 'auto', Master: 1, Sfx: 1, Music: 0.5, Muted: false, ReduceMotion: false, View3d: true, HasView3d: true, ...extra });
const stored = page => page.evaluate(() => JSON.parse(localStorage.getItem('boardempire.settings') || '{}'));

async function open(run, ctxOpts, settings, cores = 0) {
  const ctx = await browser.newContext(ctxOpts);
  // Desktop claims 8 cores so Auto picks the WebGL hero as on a real desktop (this host has 2).
  if (cores) await ctx.addInitScript(n => Object.defineProperty(Navigator.prototype, 'hardwareConcurrency', { get: () => n, configurable: true }), cores);
  await ctx.addInitScript(s => {
    try { if (!sessionStorage.getItem('e2e-init')) { sessionStorage.setItem('e2e-init', '1'); localStorage.setItem('boardempire.settings', s); } } catch { }
  }, settings);
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => {
    if (m.type() !== 'error' && m.type() !== 'warning') return;
    const t = m.text();
    if (!BENIGN.some(r => r.test(t))) errors.push(m.type() + ': ' + t.slice(0, 300));
  });
  const shot = name => page.screenshot({ path: path.join(outDir, `${run}-${name}.png`), timeout: 60000 }).catch(e => console.log('shot failed', name, e.message.split('\n')[0]));
  return { ctx, page, errors, shot };
}

// Colours and luminance spread of the 3D canvas, from a screenshot (preserveDrawingBuffer is off).
async function canvasSample(page) {
  const buf = await page.locator('canvas.board3d').screenshot({ timeout: 60000 });
  return page.evaluate(async b64 => {
    const img = new Image(); img.src = 'data:image/png;base64,' + b64; await img.decode();
    const c = new OffscreenCanvas(img.width, img.height), g = c.getContext('2d'); g.drawImage(img, 0, 0);
    const d = g.getImageData(0, 0, img.width, img.height).data, s = new Set();
    let n = 0, sum = 0, sq = 0;
    for (let i = 0; i < d.length; i += 16) {
      s.add((d[i] << 16) | (d[i + 1] << 8) | d[i + 2]);
      const l = 0.2126 * d[i] + 0.7152 * d[i + 1] + 0.0722 * d[i + 2]; n++; sum += l; sq += l * l;
    }
    const mean = sum / n;
    return { colours: s.size, mean: Math.round(mean), sd: Math.round(Math.sqrt(sq / n - mean * mean)) };
  }, buf.toString('base64'));
}
const nonUniform = s => s.colours >= 64 && s.sd >= 8;

// After a theme switch: no element in the page may still be transitioning a colour towards (or stuck at) the
// old theme. Compare computed colours as they are with the same colours when transitions are switched off.
async function stuckColours(page) {
  return page.evaluate(async () => {
    const props = ['color', 'background-color', 'border-top-color', 'border-bottom-color', 'outline-color', 'fill', 'stroke'];
    const els = [...document.querySelectorAll('main *, main')].filter(e => e.getClientRects().length);
    const read = () => els.map(e => { const cs = getComputedStyle(e); return props.map(p => cs.getPropertyValue(p)).join('|'); });
    const before = read();
    const st = document.createElement('style');
    st.textContent = '*,*::before,*::after{transition:none!important}';
    document.head.appendChild(st);
    const after = read();
    st.remove();
    const bad = [];
    els.forEach((e, i) => { if (before[i] !== after[i]) bad.push(`${e.tagName.toLowerCase()}.${[...e.classList].join('.')}: ${before[i]} -> ${after[i]}`); });
    return { checked: els.length, bad: bad.slice(0, 6), count: bad.length };
  });
}

async function closeModals(page) {
  for (let i = 0; i < 4 && await page.locator('[aria-modal="true"]').count(); i++) { await page.keyboard.press('Escape'); await page.waitForTimeout(300); }
}

// Pick an on-screen 3D tile near the bottom centre through the board3d test hooks; returns client coordinates.
async function tile3d(page) {
  await page.waitForFunction(async () => (await import('/js/board3d.js')).__test.settled(), null, { timeout: 30000, polling: 250 }).catch(() => { });
  return page.evaluate(async () => {
    const T = (await import('/js/board3d.js')).__test;
    const r = document.querySelector('canvas.board3d').getBoundingClientRect();
    let best = null;
    for (let i = 0; i < 64; i++) {
      const n = T.tileNdc(i);
      if (!n || Math.abs(n.x) > 0.85 || Math.abs(n.y) > 0.85) continue;
      const d = Math.abs(n.x) + Math.abs(n.y + 0.4);
      if (!best || d < best.d) best = { i, d, n };
    }
    if (!best) return null;
    const x = r.left + (best.n.x + 1) / 2 * r.width, y = r.top + (1 - best.n.y) / 2 * r.height;
    return { tile: best.i, x: Math.round(x), y: Math.round(y), pick: T.pickAt(x, y) };
  });
}

async function startBots(page, name) {
  await page.goto(base, { waitUntil: 'networkidle', timeout: 60000 });
  await page.locator('input[placeholder="Guest"]').fill(name);
  await page.getByRole('button', { name: 'Play vs bots' }).first().click();
  await page.waitForURL(/\/play\//, { timeout: 60000 });
}

// ---------- desktop ----------
{
  const run = 'desktop';
  const { ctx, page, errors, shot } = await open(run, { viewport: { width: 1280, height: 800 } }, SETTINGS(), 8);
  try {
    await page.goto(base, { waitUntil: 'networkidle', timeout: 60000 });
    await page.waitForFunction(() => document.documentElement.dataset.theme, null, { timeout: 30000 }).catch(() => { });
    check(run, 'cold load applies the saved theme', await page.evaluate(() => document.documentElement.dataset.theme) === 'neon-noir');
    const chipDot = await page.locator('.theme-chips button[role=radio]', { hasText: 'Frost' }).evaluate(e => getComputedStyle(e).getPropertyValue('--sw-accent').trim());
    check(run, 'theme chips carry real palette colours', chipDot === '#7dd3fc', chipDot);
    await page.locator('.theme-chips button[role=radio]', { hasText: 'Frost' }).click();
    await page.waitForTimeout(300);
    const t = await page.evaluate(() => document.documentElement.dataset.theme);
    check(run, 'theme chip applies data-theme', t === 'frost', t);
    check(run, 'theme chip persists', (await stored(page)).Theme === 'frost');

    // WebGL context loss on the Home hero swaps in the CSS hero
    const gl = await page.waitForSelector('canvas.herobg-gl.ready', { timeout: 30000 }).catch(() => null);
    check(run, 'Home hero runs WebGL (8 cores, 1280 px)', !!gl);
    if (gl) {
      await page.evaluate(() => { const c = document.querySelector('canvas.herobg-gl'); (c.getContext('webgl2') || c.getContext('webgl')).getExtension('WEBGL_lose_context').loseContext(); });
      const css = await page.waitForSelector('.herobg.mode-css', { timeout: 8000 }).catch(() => null);
      check(run, 'hero context loss falls back to the CSS hero', !!css && await page.locator('canvas.herobg-gl').count() === 0);
      await shot('00-home-hero-css-after-loss');
    }

    await startBots(page, 'E2E');
    await page.waitForSelector('canvas.board3d', { timeout: 60000 });
    await page.waitForTimeout(2500);
    await closeModals(page);
    const s1 = await canvasSample(page);
    check(run, '3D canvas renders a non-uniform image', nonUniform(s1), s1);
    await shot('01-play-3d');

    // tile click in 3D -> property card
    const pt = await tile3d(page);
    if (pt) await page.mouse.click(pt.x, pt.y);
    const card3 = await page.waitForSelector('.pcard[aria-modal="true"]', { timeout: 6000 }).catch(() => null);
    check(run, '3D tile click opens the property card', !!card3, pt);
    if (card3) { await page.waitForTimeout(500); await shot('02-card-3d'); }
    await closeModals(page);

    // 3D -> 2D
    await page.getByRole('radio', { name: '2D' }).first().click();
    const b2 = await page.waitForSelector('.board2d', { timeout: 10000 }).catch(() => null);
    check(run, 'toggle 3D -> 2D shows the 2D board', !!b2 && await page.locator('canvas.board3d').count() === 0);
    check(run, 'board view persisted (2D)', (await stored(page)).View3d === false);
    await page.waitForTimeout(800);
    await page.locator('.board2d .tile[data-tile="1"]').click();
    const card2 = await page.waitForSelector('.pcard[aria-modal="true"]', { timeout: 6000 }).catch(() => null);
    check(run, '2D tile click opens the property card', !!card2);
    if (card2) { await page.waitForTimeout(500); await shot('03-card-2d'); }
    await closeModals(page);
    await shot('04-play-2d');

    // settings sheet: theme during Play, quality, mute
    await page.locator('.board-tools button[aria-label="Settings"]').click();
    await page.waitForSelector('[role=dialog]', { timeout: 5000 });
    await page.waitForTimeout(400);
    await page.locator('[role=dialog] [role=radiogroup][aria-label="Theme"] button', { hasText: 'Sakura' }).click();
    await page.waitForTimeout(700);   // longer than any colour transition (.3 s)
    const t2 = await page.evaluate(() => document.documentElement.dataset.theme);
    check(run, 'settings sheet theme applies during Play', t2 === 'sakura', t2);
    const stuck = await stuckColours(page);
    check(run, 'every Play surface took the new theme colours (no stuck transition)', stuck.count === 0, stuck);
    await page.locator('[role=dialog] [role=radiogroup][aria-label="Graphics quality"] button', { hasText: 'Low' }).click();
    await page.locator('[role=dialog] label.check', { hasText: 'Mute all sound' }).click();
    await page.waitForTimeout(300);
    const st = await stored(page);
    check(run, 'quality and mute saved', st.Quality === 'low' && st.Muted === true, { Quality: st.Quality, Muted: st.Muted });
    await shot('05-settings-sakura');
    await page.keyboard.press('Escape');
    await page.waitForTimeout(400);
    // the 2D board under the new theme, and the switch back to 3D
    await page.getByRole('radio', { name: '3D' }).first().click();
    const c3 = await page.waitForSelector('canvas.board3d', { timeout: 10000 }).catch(() => null);
    check(run, 'toggle 2D -> 3D shows the 3D board', !!c3);
    await page.waitForTimeout(2500);
    const s2 = await canvasSample(page);
    check(run, '3D canvas non-uniform after the switch back (Low, Sakura)', nonUniform(s2), s2);
    await shot('06-play-3d-sakura-low');
    // M toggles mute
    await page.locator('body').click({ position: { x: 5, y: 5 } }).catch(() => { });
    await page.keyboard.press('m');
    await page.waitForTimeout(300);
    check(run, 'M shortcut toggles mute', (await stored(page)).Muted === false);

    // WebGL context loss on the Play board: 2D board plus a toast
    await page.evaluate(() => { const c = document.querySelector('canvas.board3d'); (c.getContext('webgl2') || c.getContext('webgl')).getExtension('WEBGL_lose_context').loseContext(); });
    const fell = await page.waitForSelector('.board2d', { timeout: 8000 }).catch(() => null);
    const toast = await page.locator('.toast', { hasText: 'context lost' }).count();
    check(run, '3D context loss falls back to 2D with a toast', !!fell && toast > 0, { board2d: !!fell, toast });
    await shot('07-context-lost-2d');
    check(run, 'context loss does not overwrite the saved 3D preference', (await stored(page)).View3d === true);

    // persisted across a reload
    await page.goto(base, { waitUntil: 'networkidle', timeout: 60000 });
    await page.waitForFunction(() => document.documentElement.dataset.theme, null, { timeout: 30000 }).catch(() => { });
    const after = await page.evaluate(() => ({ theme: document.documentElement.dataset.theme, s: JSON.parse(localStorage.getItem('boardempire.settings') || '{}') }));
    check(run, 'theme, quality, view and mute survive a reload', after.theme === 'sakura' && after.s.Quality === 'low' && after.s.Muted === false && after.s.View3d === true, after);
  } catch (e) {
    check(run, 'run completed', false, e.message.split('\n')[0]);
    await shot('zz-failure');
  }
  check(run, 'no console errors', errors.length === 0, [...new Set(errors)].slice(0, 5).join(' | '));
  await ctx.close();
}

// ---------- phone ----------
{
  const run = 'phone';
  const { ctx, page, errors, shot } = await open(run, { viewport: { width: 390, height: 844 }, deviceScaleFactor: 3, isMobile: true, hasTouch: true,
    userAgent: 'Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36' },
  SETTINGS({ View3d: false, Theme: 'classic-gold', Muted: true }));
  const overflow = async label => {
    const o = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: innerWidth }));
    check(run, `no horizontal overflow (${label})`, o.sw <= o.iw, o);
  };
  try {
    await page.goto(base, { waitUntil: 'networkidle', timeout: 60000 });
    await overflow('home');
    await page.locator('.theme-chips button[role=radio]', { hasText: 'Neon Noir' }).tap();
    await page.waitForTimeout(300);
    check(run, 'theme chip tap applies data-theme', await page.evaluate(() => document.documentElement.dataset.theme) === 'neon-noir');
    await page.locator('input[placeholder="Guest"]').fill('Phone');
    await page.getByRole('button', { name: 'Play vs bots' }).first().tap();
    await page.waitForURL(/\/play\//, { timeout: 60000 });
    await page.waitForSelector('.board2d', { timeout: 60000 });
    await page.waitForTimeout(1500);
    await closeModals(page);
    await overflow('play 2D');
    const r = await page.locator('.board2d .tile[data-tile="3"]').boundingBox();
    await page.touchscreen.tap(r.x + r.width / 2, r.y + r.height / 2);
    const card = await page.waitForSelector('.pcard[aria-modal="true"]', { timeout: 6000 }).catch(() => null);
    check(run, '2D tile tap opens the property card', !!card);
    if (card) { await page.waitForTimeout(500); await shot('01-card-2d'); }
    await closeModals(page);
    await shot('02-play-2d');
    // 3D through the toolbar toggle
    await page.locator('.board-tools [role=radio]', { hasText: '3D' }).tap();
    const c3 = await page.waitForSelector('canvas.board3d', { timeout: 10000 }).catch(() => null);
    check(run, 'toggle 2D -> 3D (touch)', !!c3);
    await page.waitForTimeout(3000);
    const s = await canvasSample(page);
    check(run, '3D canvas renders a non-uniform image (phone)', nonUniform(s), s);
    await overflow('play 3D');
    await shot('03-play-3d');
  } catch (e) {
    check(run, 'run completed', false, e.message.split('\n')[0]);
    await shot('zz-failure');
  }
  check(run, 'no console errors', errors.length === 0, [...new Set(errors)].slice(0, 5).join(' | '));
  await ctx.close();
}

await browser.close();
let failed = 0;
for (const r of results) {
  if (!r.ok) failed++;
  console.log(`${r.ok ? 'ok  ' : 'FAIL'} [${r.run}] ${r.name}${r.detail && (!r.ok || process.env.VERBOSE) ? ' — ' + r.detail : ''}`);
}
console.log(`${results.length - failed}/${results.length} checks passed; screenshots in ${outDir}`);
process.exit(failed ? 1 : 0);
