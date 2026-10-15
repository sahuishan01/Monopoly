// Direct-import render check for the 3D board (no Blazor, no server).
// Serves the repo statically, loads wwwroot/js/board3d.js in headless Chromium,
// renders every theme at low/high quality plus a re-init, and writes screenshots.
//
//   node tools/web-smoke/render-check.mjs [outDir] [--gl=egl|swiftshader] [--js=<dir with board3d.js>]
//        [--board=pocket_city] [--debug]
// Set CHROMIUM_PATH to use a browser other than the one bundled with this playwright version.
//
// Exit code 1 if a render is blank, the WebGL context was lost, or the console logged errors/warnings.
// Software GL backends (no GPU), always with --no-sandbox --disable-gpu-sandbox --ignore-gpu-blocklist:
//   swiftshader: --use-gl=angle --use-angle=swiftshader-webgl --enable-unsafe-swiftshader
//   egl:         --use-gl=angle --use-angle=gl-egl   (system Mesa llvmpipe; needs mesa-libEGL + mesa-dri-drivers)
// Default is egl on arm64 and swiftshader elsewhere: on aarch64 Linux the SwiftShader bundled with
// Chromium segfaults the GPU process on the first draw call (sw::DrawCall::processVertices), which
// shows up as blank canvases, "Shader Error 0 - VALIDATE_STATUS false" and a lost context.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const args = process.argv.slice(2);
const opt = k => (args.find(a => a.startsWith(`--${k}=`)) || '').split('=').slice(1).join('=') || null;
const outDir = path.resolve(args.find(a => !a.startsWith('--')) || path.join(repo, 'tools/web-smoke/render-out'));
const jsDir = path.resolve(opt('js') || path.join(repo, 'src/Game.Web/wwwroot/js'));
const boardId = opt('board') || 'pocket_city';
const themeDir = path.join(repo, 'src/Game.Web/wwwroot/themes');
fs.mkdirSync(outDir, { recursive: true });

// --debug: log the real GLSL/link error (three only prints VALIDATE_STATUS) and context loss.
const DEBUG = !args.includes('--debug') ? '' : `<script>
for (const C of [WebGLRenderingContext, WebGL2RenderingContext]) {
  const link = C.prototype.linkProgram;
  C.prototype.linkProgram = function (p) {
    link.call(this, p);
    if (this.getProgramParameter(p, this.LINK_STATUS)) return;
    const sh = (this.getAttachedShaders(p) || []).map(s => 'compile=' + this.getShaderParameter(s, this.COMPILE_STATUS) + ' log=' + this.getShaderInfoLog(s));
    console.error('LINK FAIL lost=' + this.isContextLost() + ' err=' + this.getError() + ' log=' + this.getProgramInfoLog(p) + ' | ' + sh.join(' | '));
  };
}
addEventListener('webglcontextlost', e => console.error('CONTEXT LOST event'), true);
</script>`;
const PAGE = `<!doctype html><meta charset=utf-8><style>html,body{margin:0;background:#000}canvas{width:900px;height:640px;display:block}</style>
<canvas id=c></canvas>${DEBUG}<script type=module>
import * as B from '/js/board3d.js';
window.B = B;
const board = await (await fetch('/board.json')).json();
const themes = await (await fetch('/themes.json')).json();
const n = board.Tiles.length;
const state = {
  Players: [0, 1, 2, 3].map(i => ({ Id: i, Position: [0, 7, 15, 24][i] % n, Bankrupt: false })),
  Properties: board.Tiles.map((t, i) => t.IsOwnable ? { Owner: i % 3 === 0 ? i % 4 : -1, Level: i % 7 === 1 ? 3 : i % 11 === 3 ? 5 : 0, Mortgaged: i % 13 === 5 } : null),
};
window.H = { board, themes, state };
window.ready = true;
</script>`;

const types = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.html': 'text/html' };
const server = http.createServer((req, res) => {
  const u = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  let body, file;
  if (u === '/') body = PAGE;
  else if (u === '/favicon.ico') { res.writeHead(204); return res.end(); }
  else if (u === '/board.json') file = path.join(repo, 'boards', boardId + '.json');
  else if (u === '/themes.json') body = JSON.stringify(fs.readdirSync(themeDir).filter(f => f.endsWith('.json')).sort()
    .map(f => JSON.parse(fs.readFileSync(path.join(themeDir, f), 'utf8'))));
  else if (u.startsWith('/js/')) { file = path.join(jsDir, u.slice(4)); if (!file.startsWith(jsDir)) file = null; }
  if (file && fs.existsSync(file)) body = fs.readFileSync(file);
  if (body === undefined) { res.writeHead(404); return res.end(); }
  res.writeHead(200, { 'content-type': types[file ? path.extname(file) : (u.endsWith('.json') ? '.json' : '.html')] || 'application/octet-stream' });
  res.end(body);
}).listen(0, '127.0.0.1');
await new Promise(r => server.once('listening', r));
const base = `http://127.0.0.1:${server.address().port}/`;

const GL = (opt('gl') || (process.arch === 'arm64' ? 'egl' : 'swiftshader')) === 'egl' ? ['--use-gl=angle', '--use-angle=gl-egl'] // Mesa llvmpipe through EGL
  : ['--use-gl=angle', '--use-angle=swiftshader-webgl', '--enable-unsafe-swiftshader'];
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined,
  args: [...GL, '--no-sandbox', '--disable-gpu-sandbox', '--ignore-gpu-blocklist'] });
const page = await browser.newPage({ viewport: { width: 900, height: 640 } });
const errors = [];
page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errors.push(`[${m.type()}] ${m.text()}`); });
page.on('pageerror', e => errors.push(`[pageerror] ${e.message}`));
await page.goto(base);
await page.waitForFunction(() => window.ready, null, { timeout: 30000 });

// Colour count from the canvas pixels (preserveDrawingBuffer is off, so read the screenshot).
// A crashed canvas still shows Chrome's small broken-image icon (~20 colours), hence the 64 floor.
const MIN_COLOURS = 64;
async function shot(name) {
  await page.waitForTimeout(1200);
  const lost = await page.evaluate(() => { const c = document.getElementById('c'); const g = c.getContext('webgl2') || c.getContext('webgl'); return !g || g.isContextLost(); });
  const file = path.join(outDir, name + '.png');
  const buf = await page.locator('#c').screenshot({ path: file });
  const colours = await page.evaluate(async b64 => {
    const img = new Image(); img.src = 'data:image/png;base64,' + b64; await img.decode();
    const c = new OffscreenCanvas(img.width, img.height), g = c.getContext('2d'); g.drawImage(img, 0, 0);
    const d = g.getImageData(0, 0, img.width, img.height).data, s = new Set();
    for (let i = 0; i < d.length; i += 16) s.add((d[i] << 16) | (d[i + 1] << 8) | d[i + 2]);
    return s.size;
  }, buf.toString('base64'));
  return { name, colours, lost, bad: lost || colours < MIN_COLOURS };
}

const results = [];
const themes = await page.evaluate(() => H.themes.map(t => t.id));
for (const q of ['low', 'high']) {
  const ok = await page.evaluate(q => B.init(document.getElementById('c'), JSON.stringify(H.board), null,
    JSON.stringify({ quality: q, theme: H.themes[0] })), q);
  if (!ok) errors.push(`init(${q}) returned false`);
  await page.evaluate(() => B.applyState(JSON.stringify(H.state)));
  for (const id of themes) {
    await page.evaluate(id => B.setTheme && B.setTheme(JSON.stringify(H.themes.find(t => t.id === id))), id);
    results.push(await shot(`${id}-${q}`));
  }
}
// re-init after dispose
const again = await page.evaluate(() => { B.dispose(); const ok = B.init(document.getElementById('c'), JSON.stringify(H.board), null, JSON.stringify({ quality: 'high', theme: H.themes[0] })); B.applyState(JSON.stringify(H.state)); return ok; });
if (!again) errors.push('re-init returned false');
results.push(await shot('reinit-high'));
await page.evaluate(() => B.dispose());

await browser.close();
server.close();
console.log('renderer:', GL.join(' '));
for (const r of results) console.log(`${r.bad ? 'FAIL ' : 'ok   '} ${r.name.padEnd(22)} ${r.colours} colours${r.lost ? ' CONTEXT LOST' : ''}`);
console.log(errors.length ? `console (${errors.length}):\n` + [...new Set(errors)].map(e => '  ' + e.slice(0, 400)).join('\n') : 'console: clean');
console.log('screenshots in', outDir);
process.exit(results.some(r => r.bad) || errors.length ? 1 : 0);
