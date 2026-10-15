// Mobile layout check for the web client, in real Chromium with phone device emulation (touch, DPR, mobile UA).
// For every phone profile and board view it starts a bot match, then checks:
//   * no horizontal overflow (document scrollWidth <= innerWidth);
//   * the primary action (Roll) is visible in the collapsed HUD: >= 44 x 44 px, inside the viewport, not covered;
//   * one turn played with taps only: tap Roll, Buy from the offer card or tap End turn;
//   * tapping a board tile opens the property card as a bottom sheet;
//   * the HUD bottom sheet expands and collapses by tap and by touch drag (portrait), or is a side rail (landscape);
//   * every visible button / input / select in the HUD, toolbar, peek strip and open sheets is >= 44 x 44 px;
//   * 3D: the canvas is fully on screen and Auto quality picked the Low tier (backing store capped at 1.5x DPR);
//   * no console errors or page errors.
// Screenshots go to outDir (look at them; the checks cannot judge polish).
//
//   node tools/web-smoke/mobile-check.mjs [outDir] [--base=http://127.0.0.1:5198/] [--serve=<wwwroot> --api=<port>]
//        [--devices=pixel7,iphone,small,landscape] [--views=2d,3d] [--gl=egl|swiftshader]
//
// The app must be reachable at --base with /api and /ws routed to a Game.Server. Either point --base at a running
// stack, or pass --serve=<published wwwroot> --api=<Game.Server port> and this script serves the static files and
// proxies /api, /healthz and WebSocket upgrades itself on --base's port. Start the server for example with:
//   dotnet publish src/Game.Web -c Release -o /tmp/pub
//   BoardEmpire__TokenSecret=<32+ chars> BoardEmpire__BotDelaySeconds=0.3 ASPNETCORE_URLS=http://127.0.0.1:5199 \
//     dotnet src/Game.Server/bin/Release/net8.0/Game.Server.dll
//   node tools/web-smoke/mobile-check.mjs out --serve=/tmp/pub/wwwroot --api=5199
// Set CHROMIUM_PATH to use a browser other than the one bundled with this playwright version. WebGL uses
// --gl=egl by default on arm64 (SwiftShader crashes there) and swiftshader elsewhere, as in render-check.mjs.
// Exit code 1 when any check fails.
import http from 'node:http';
import net from 'node:net';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const args = process.argv.slice(2);
const opt = k => (args.find(a => a.startsWith(`--${k}=`)) || '').split('=').slice(1).join('=') || null;
const outDir = path.resolve(args.find(a => !a.startsWith('--')) || path.join(repo, 'tools/web-smoke/mobile-out'));
const base = opt('base') || 'http://127.0.0.1:5198/';
fs.mkdirSync(outDir, { recursive: true });

const DEVICES = {
  pixel7: { viewport: { width: 412, height: 915 }, deviceScaleFactor: 2.625 },
  iphone: { viewport: { width: 375, height: 667 }, deviceScaleFactor: 2 },
  small: { viewport: { width: 360, height: 640 }, deviceScaleFactor: 3 },
  phone390: { viewport: { width: 390, height: 844 }, deviceScaleFactor: 3 },
  landscape: { viewport: { width: 844, height: 390 }, deviceScaleFactor: 3 },
};
const devices = (opt('devices') || 'pixel7,iphone,small,landscape').split(',');
const views = (opt('views') || '2d,3d').split(',');
const UA = 'Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36';

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

async function runOne(devName, view) {
  const dev = DEVICES[devName];
  if (!dev) throw new Error('unknown device ' + devName);
  const run = `${devName}-${view}`;
  const ctx = await browser.newContext({ ...dev, isMobile: true, hasTouch: true, userAgent: UA });
  await ctx.addInitScript(v => {
    try {
      if (!sessionStorage.getItem('mc-init')) {
        sessionStorage.setItem('mc-init', '1');
        localStorage.setItem('boardempire.settings', JSON.stringify({ Theme: 'neon-noir', Quality: 'auto', Master: 1, Sfx: 1, Music: 0.5, Muted: true, ReduceMotion: false, View3d: v === '3d' }));
      }
    } catch { }
  }, view);
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errors.push(m.type() + ': ' + m.text().slice(0, 300)); });
  const shot = async name => { try { await page.screenshot({ path: path.join(outDir, `${run}-${name}.png`), timeout: 60000 }); } catch (e) { console.log('shot failed', run, name, e.message.split('\n')[0]); } };
  const wait = ms => page.waitForTimeout(ms);
  const vw = dev.viewport.width, vh = dev.viewport.height, landscape = vw > vh;

  const overflow = async label => {
    const o = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, bw: document.body.scrollWidth, iw: innerWidth }));
    check(run, `no horizontal overflow (${label})`, o.sw <= o.iw && o.bw <= o.iw, JSON.stringify(o));
  };
  // Every visible control in `scope` must be >= 44 x 44 (checkbox/radio/range are judged by their label).
  const targets = async (scope, label) => {
    const small = await page.evaluate(scope => {
      const out = [];
      for (const root of document.querySelectorAll(scope)) {
        for (const el of root.querySelectorAll('button, input, select, textarea, a[href], [role=button]')) {
          if (el.closest('[inert]')) continue;
          const cs = getComputedStyle(el);
          if (cs.display === 'none' || cs.visibility === 'hidden') continue;
          let t = el;
          if (el.matches('input[type=checkbox], input[type=radio]')) t = el.closest('label') || el;
          const r = t.getBoundingClientRect();
          if (r.width === 0 && r.height === 0) continue;
          if (r.width < 43.5 || r.height < 43.5) out.push(`${(el.getAttribute('aria-label') || el.textContent || el.id || el.tagName).trim().slice(0, 24)} ${Math.round(r.width)}x${Math.round(r.height)}`);
        }
      }
      return out;
    }, scope);
    check(run, `touch targets >= 44px (${label})`, small.length === 0, small.join('; '));
  };
  const primaryVisible = async label => {
    const b = page.locator('.hud-peek .peek-action button').first();
    const info = await b.evaluate(el => {
      const r = el.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top + r.height / 2;
      const hit = document.elementFromPoint(x, y);
      return { w: r.width, h: r.height, l: r.left, t: r.top, r: r.right, b: r.bottom, text: el.textContent.trim(), onTop: !!hit && (hit === el || el.contains(hit)) };
    }).catch(() => null);
    const ok = info && info.w >= 44 && info.h >= 44 && info.l >= 0 && info.t >= 0 && info.r <= vw && info.b <= vh && info.onTop;
    check(run, `primary action visible + tappable (${label})`, ok, JSON.stringify(info));
    return ok;
  };
  // Tap the peek strip's action. It is disabled while events animate, so retry until it takes (or give up).
  const tapPrimary = async (text, ms = 15000) => {
    const b = page.locator(`.hud-peek .peek-action button:has-text("${text}")`).first();
    const end = Date.now() + ms;
    while (Date.now() < end) {
      if (await b.count() && await b.isEnabled().catch(() => false)) {
        if (await b.tap({ timeout: 1500 }).then(() => true, () => false)) return true;
      }
      await wait(200);
    }
    return false;
  };
  // Touch drag on the peek handle via CDP (real touchstart/move/end, so pointer events have pointerType touch).
  const cdp = await ctx.newCDPSession(page);
  const drag = async dy => {
    const r = await page.locator('.peek-toggle').boundingBox();
    const x = r.x + r.width * 0.35, y0 = r.y + r.height / 2;
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y: y0 }] });
    // move slowly: under software GL a 3D frame can take long enough that fast moves get coalesced away
    for (let i = 1; i <= 12; i++) { await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x, y: y0 + dy * i / 12 }] }); await wait(40); }
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
    await settle();
  };
  // wait until the sheet stops moving (its transition; software GL can make frames slow)
  const settle = async () => {
    await wait(450);
    let last = null, still = 0;
    for (let i = 0; i < 40 && still < 2; i++) {
      await wait(120);
      const t = await page.evaluate(() => document.querySelector('.side').getBoundingClientRect().top);
      still = last !== null && Math.abs(t - last) < 0.5 ? still + 1 : 0;
      last = t;
    }
  };
  const hudOpen = () => page.evaluate(() => !!document.querySelector('main.play.hud-open'));

  try {
    await page.goto(base, { waitUntil: 'networkidle', timeout: 60000 });
    await overflow('home');
    await shot('00-home');
    await page.locator('input[placeholder="Guest"]').fill('Ishan');
    await page.locator('button:has-text("Play vs bots")').first().tap();
    await page.waitForURL(/\/play\//, { timeout: 60000 });
    await page.waitForSelector(view === '3d' ? 'canvas.board3d' : '.board2d', { timeout: 60000 });
    await wait(1500);
    for (let i = 0; i < 3 && await page.locator('[aria-modal="true"]').count(); i++) { await page.keyboard.press('Escape'); await wait(300); }

    // layout mode, as ui.js reported it to the page
    const mode = await page.evaluate(() => getComputedStyle(document.querySelector('.side')).getPropertyValue('--hud-sheet').trim());
    check(run, `layout mode ${landscape ? 'rail' : 'sheet'}`, landscape ? mode === '0' : mode === '1', `--hud-sheet=${mode}`);

    // board sizing: fully on screen, not under the collapsed sheet, fills the free space
    const board = await page.evaluate(sel => {
      const r = document.querySelector(sel).getBoundingClientRect();
      const side = document.querySelector('.side').getBoundingClientRect();
      const peek = document.querySelector('.hud-peek').getBoundingClientRect();
      return { l: r.left, t: r.top, r: r.right, b: r.bottom, w: r.width, h: r.height, sideTop: side.top, peekTop: peek.top, peekH: peek.height };
    }, view === '3d' ? 'canvas.board3d' : '.b2d');
    check(run, 'board fully on screen', board.l >= 0 && board.t >= 0 && board.r <= vw + 0.5 && board.b <= vh + 0.5, JSON.stringify(board));
    if (!landscape) {
      check(run, 'board clear of collapsed sheet', board.b <= board.peekTop + 0.5, `board bottom ${board.b} peek top ${board.peekTop}`);
      check(run, 'collapsed sheet ~72px', board.peekH >= 64 && board.peekH <= 84 && vh - board.peekTop <= 90, `peek h ${board.peekH}, visible ${vh - board.peekTop}`);
      check(run, 'board uses the width', board.w >= vw - 32, `w ${board.w} of ${vw}`);
    } else {
      check(run, 'board uses the height', board.h >= vh - 24, `h ${board.h} of ${vh}`);
    }
    if (view === '3d') {
      const q = await page.evaluate(() => { const c = document.querySelector('canvas.board3d'); return { bw: c.width, cw: c.clientWidth, dpr: devicePixelRatio }; });
      // Low caps the backing store at 1.5x DPR (High allows 2x); every profile here has DPR >= 2.
      check(run, '3D auto quality is Low on a phone (backing store <= 1.5x)', q.bw <= q.cw * 1.5 + 2, JSON.stringify(q));
    }
    await overflow('play');

    // wait for our turn: Roll in the peek strip
    const rollDeadline = Date.now() + 60000;
    while (Date.now() < rollDeadline) {
      if (await page.locator('.hud-peek .peek-action button:has-text("Roll"):enabled').count()) break;
      if (await page.locator('.hud-peek .peek-action button:has-text("End turn"):enabled').count()) await tapPrimary('End turn', 3000);
      await wait(250);
    }
    await wait(300);
    await shot('01-collapsed-roll');
    if (await primaryVisible('collapsed, Roll')) {
      // play a turn with taps
      const played = { roll: false, buy: false, pass: false, end: false };
      played.roll = await tapPrimary('Roll', 20000);
      const end = Date.now() + 60000;
      let offerShot = false;
      while (Date.now() < end && !played.end) {
        await wait(300);
        const buy = page.locator('.pcard .pc-actions button:has-text("Buy for")');
        if (await buy.count()) {
          if (!offerShot) { offerShot = true; await wait(500); await shot('02-offer-card'); await targets('.sheet-panel', 'offer card'); }
          await buy.first().tap({ timeout: 2000 }).catch(() => { });
          played.buy = true;
          continue;
        }
        if (await page.locator('[aria-modal="true"]').count()) { await page.keyboard.press('Escape'); continue; }
        if (await page.locator('.hud-peek .peek-action button:has-text("Buy"):enabled').count()) { played.buy = await tapPrimary('Buy', 3000); continue; }
        if (await page.locator('.hud-peek .peek-action button:has-text("End turn"):enabled').count()) {
          await primaryVisible('collapsed, End turn');
          await shot('03-end-turn');
          played.end = await tapPrimary('End turn', 5000);
          continue;
        }
        // the turn can also end without End turn (sent to jail): the peek then shows another player's turn
        if (played.roll && await page.locator('.hud-peek:not(.mine) .peek-wait').count()) { played.end = 'by rules'; break; }
        // rolled doubles: roll again
        if (await page.locator('.hud-peek .peek-action button:has-text("Roll"):enabled').count()) await tapPrimary('Roll', 3000);
      }
      // A tap can land and still report a timeout when the button re-renders under it; reaching the
      // buy/end phase proves the Roll tap went through.
      if (!played.roll && (played.buy || played.end)) played.roll = 'inferred';
      check(run, 'played a turn with taps (Roll ... End turn)', played.roll && played.end, JSON.stringify(played));
    }

    // tap a tile -> property card sheet
    let tapInfo = '';
    for (let i = 0; i < 3 && await page.locator('[aria-modal="true"]').count(); i++) { await page.keyboard.press('Escape'); await wait(300); }
    let card = null;
    for (let attempt = 0; attempt < 4 && !card; attempt++) {
      if (attempt) tapInfo += ' (retry)';
      if (view === '3d') {
        // The camera eases (follow cam on each bot turn): wait until it is still, then tap the on-screen tile
        // nearest the bottom centre. A touch holds the auto camera for 4 s, so a retry aims at a still board.
        await page.waitForFunction(async () => {
          const B = await import('/js/board3d.js');
          const a = JSON.stringify(B.framing());
          await new Promise(r => setTimeout(r, 400));
          return a === JSON.stringify(B.framing());
        }, null, { timeout: 15000, polling: 500 }).catch(() => { });
        const pt = await page.evaluate(async () => {
          const B = await import('/js/board3d.js');
          const r = document.querySelector('canvas.board3d').getBoundingClientRect();
          let best = null;
          for (let i = 0; i < 64; i++) {
            const n = B.tileNdc(i);
            if (!n || Math.abs(n.x) > 0.85 || Math.abs(n.y) > 0.85) continue;
            const d = Math.abs(n.x) + Math.abs(n.y + 0.4);
            if (!best || d < best.d) best = { i, d, n };
          }
          if (!best) return null;
          const x = r.left + (best.n.x + 1) / 2 * r.width, y = r.top + (1 - best.n.y) / 2 * r.height;
          return { tile: best.i, x: Math.round(x), y: Math.round(y), pick: B.pickAt(x, y) };
        });
        tapInfo += ' ' + JSON.stringify(pt);
        if (pt) await page.touchscreen.tap(pt.x, pt.y);
      } else {
        const r = await page.locator('.board2d .tile[data-tile="3"]').boundingBox();
        await page.touchscreen.tap(r.x + r.width / 2, r.y + r.height / 2);
      }
      card = await page.waitForSelector('.pcard[aria-modal="true"]', { timeout: 5000 }).catch(() => null);
    }
    check(run, 'tile tap opens the property card', !!card, tapInfo);
    if (tapInfo.includes('retry')) console.log(`note [${run}] tile tap needed a retry: ${tapInfo}`);
    if (card) {
      await wait(600);
      await shot('04-tile-card');
      const sheetBox = await page.locator('.sheet-panel').boundingBox();
      if (vw <= 640) check(run, 'card is a bottom sheet', sheetBox && Math.abs(sheetBox.y + sheetBox.height - vh) < 2 && sheetBox.width >= vw - 2, JSON.stringify(sheetBox));
      await targets('.sheet-panel', 'tile card');
      await overflow('tile card');
      await page.locator('.sheet-panel .pc-close').tap().catch(() => page.keyboard.press('Escape'));
      await wait(500);
    }

    if (!landscape) {
      // expand by tap
      await page.locator('.peek-toggle').tap();
      await settle();
      check(run, 'tap expands the HUD sheet', await hudOpen());
      let sh = null;
      for (let i = 0; i < 20; i++) {   // the slide-in can lag under software GL
        sh = await page.evaluate(() => { const r = document.querySelector('.side').getBoundingClientRect(); return { t: r.top, b: r.bottom, h: r.height }; });
        if (sh.b <= vh + 1) break;
        await wait(200);
      }
      check(run, 'expanded sheet on screen', sh.t >= 0 && sh.b <= vh + 1, JSON.stringify(sh));
      await shot('05-expanded');
      await targets('.side, .board-tools', 'expanded HUD');
      await overflow('expanded');
      // scroll the sheet to its end and look again
      await page.evaluate(() => { const b = document.querySelector('.side-body'); b.scrollTop = b.scrollHeight; });
      await wait(300);
      await shot('06-expanded-scrolled');
      // collapse by dragging the handle down, re-open by dragging up, close via the scrim
      await drag(220);
      check(run, 'drag down collapses the sheet', !(await hudOpen()));
      await drag(-260);
      check(run, 'drag up expands the sheet', await hudOpen());
      await page.locator('.hud-scrim').tap({ position: { x: 20, y: 20 } });
      await wait(600);
      check(run, 'scrim tap collapses the sheet', !(await hudOpen()));
      const inert = await page.evaluate(() => document.querySelector('.side-body').hasAttribute('inert'));
      check(run, 'collapsed sheet body is inert', inert);
    } else {
      await targets('.side, .board-tools', 'rail HUD');
      await shot('05-rail');
      await page.evaluate(() => { const s = document.querySelector('.side'); s.scrollTop = s.scrollHeight; });
      await wait(300);
      const peek = await page.locator('.hud-peek').boundingBox();
      check(run, 'rail peek stays visible when scrolled', peek && peek.y >= 0 && peek.y < 40, JSON.stringify(peek));
      await shot('06-rail-scrolled');
    }

    // settings sheet touch targets (via the toolbar gear)
    for (let i = 0; i < 3 && await page.locator('[aria-modal="true"]').count(); i++) { await page.keyboard.press('Escape'); await wait(300); }
    await page.locator('.board-tools button[aria-label="Settings"]').tap();
    if (await page.waitForSelector('[aria-modal="true"]', { timeout: 4000 }).catch(() => null)) {
      await wait(600);
      await shot('07-settings');
      await targets('.sheet-panel', 'settings');
      await overflow('settings');
      await page.keyboard.press('Escape');
      await wait(400);
    }
    check(run, 'theme-color meta follows the theme', await page.evaluate(() => {
      const m = document.querySelector('meta[name="theme-color"]')?.content?.toLowerCase();
      return m && m === getComputedStyle(document.documentElement).getPropertyValue('--bg').trim().toLowerCase();
    }));
  } catch (e) {
    check(run, 'run completed', false, e.message.split('\n')[0]);
    await shot('zz-failure');
  }
  check(run, 'console clean', errors.length === 0, [...new Set(errors)].slice(0, 5).join(' | '));
  await ctx.close();
}

// PWA basics, once
{
  const ctx = await browser.newContext();
  const page = await ctx.newPage();
  await page.goto(base, { waitUntil: 'networkidle', timeout: 60000 });
  const pwa = await page.evaluate(async () => {
    const vp = document.querySelector('meta[name=viewport]')?.content || '';
    const href = document.querySelector('link[rel=manifest]')?.href;
    const m = href ? await (await fetch(href)).json() : null;
    const icons = [];
    for (const i of m?.icons || []) {
      const img = new Image(); img.src = new URL(i.src, href).href;
      try { await img.decode(); icons.push(`${i.src} ${img.naturalWidth}x${img.naturalHeight} ${i.purpose || 'any'}`); } catch { icons.push(i.src + ' BROKEN'); }
    }
    return { vp, manifest: !!m, display: m?.display, name: m?.name, icons, sw: !!navigator.serviceWorker?.controller };
  });
  check('pwa', 'viewport-fit=cover', /viewport-fit=cover/.test(pwa.vp), pwa.vp);
  check('pwa', 'manifest standalone with icons', pwa.manifest && pwa.display === 'standalone' && pwa.icons.length === 3 && !pwa.icons.some(i => i.includes('BROKEN')), JSON.stringify(pwa));
  check('pwa', 'no service worker controls the page', !pwa.sw);
  await ctx.close();
}

for (const d of devices) for (const v of views) {
  const t0 = Date.now();
  await runOne(d, v);
  console.log(`ran ${d}-${v} in ${((Date.now() - t0) / 1000).toFixed(0)} s`);
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
