// One-off generator for the PWA icons (wwwroot/icons/{192,512,maskable}.png).
// Draws a themed SVG (Neon Noir tokens: a board ring of tiles around a glowing pawn) and rasterises
// it with headless Chromium, so no image tooling is needed. Re-run after changing the art:
//
//   node tools/web-smoke/make-icons.mjs [outDir]       (default src/Game.Web/wwwroot/icons)
// Set CHROMIUM_PATH to use a browser other than the one bundled with this playwright version.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const outDir = path.resolve(process.argv[2] || path.join(repo, 'src/Game.Web/wwwroot/icons'));
fs.mkdirSync(outDir, { recursive: true });

const T = { bg: '#07080f', panel: '#111420', accent: '#22f5c4', glow: '#7c3aed', text: '#e8ecf4' };
const DISTRICTS = ['#f43f5e', '#f59e0b', '#22c55e', '#3b82f6', '#a855f7', '#ec4899', '#14b8a6', '#eab308'];

// The art, drawn on a 512 grid. `scale` shrinks it about the centre (maskable icons keep the art
// inside the 80% safe circle); `round` gives the plain icons rounded corners.
function svg({ scale = 1, round = true }) {
  const tiles = [];
  const n = 5, ring = 300, cell = ring / n, x0 = 256 - ring / 2;
  for (let i = 0; i < n * 4; i++) {
    const side = Math.floor(i / n), k = i % n;
    let c, r;
    if (side === 0) { c = k; r = 0; } else if (side === 1) { c = n; r = k; } else if (side === 2) { c = n - k; r = n; } else { c = 0; r = n - k; }
    const x = x0 + c * cell, y = x0 + r * cell;
    const corner = k === 0;
    const fill = corner ? T.panel : DISTRICTS[i % DISTRICTS.length];
    tiles.push(`<rect x="${(x - cell * .42).toFixed(1)}" y="${(y - cell * .42).toFixed(1)}" width="${(cell * .84).toFixed(1)}" height="${(cell * .84).toFixed(1)}" rx="9"
      fill="${fill}" ${corner ? `stroke="${T.accent}" stroke-width="5"` : 'filter="url(#tileGlow)"'} />`);
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512">
  <defs>
    <radialGradient id="bg" cx="50%" cy="38%" r="75%">
      <stop offset="0" stop-color="#1b1640" /><stop offset=".55" stop-color="#0c0f22" /><stop offset="1" stop-color="${T.bg}" />
    </radialGradient>
    <radialGradient id="halo" cx="50%" cy="50%" r="50%">
      <stop offset="0" stop-color="${T.accent}" stop-opacity=".55" /><stop offset="1" stop-color="${T.accent}" stop-opacity="0" />
    </radialGradient>
    <linearGradient id="pawn" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#b9fff0" /><stop offset=".5" stop-color="${T.accent}" /><stop offset="1" stop-color="#0e9e7f" />
    </linearGradient>
    <linearGradient id="felt" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#0f2a2a" /><stop offset="1" stop-color="#0a1418" />
    </linearGradient>
    <filter id="tileGlow" x="-40%" y="-40%" width="180%" height="180%"><feGaussianBlur stdDeviation="3" result="b" /><feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
    <filter id="soft" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="10" /></filter>
  </defs>
  <rect width="512" height="512" rx="${round ? 112 : 0}" fill="url(#bg)" />
  <g transform="translate(256 256) scale(${scale}) translate(-256 -256)">
    <rect x="76" y="76" width="360" height="360" rx="36" fill="url(#felt)" stroke="${T.glow}" stroke-opacity=".7" stroke-width="6" />
    <rect x="76" y="76" width="360" height="360" rx="36" fill="none" stroke="${T.glow}" stroke-width="14" stroke-opacity=".35" filter="url(#soft)" />
    ${tiles.join('\n    ')}
    <circle cx="256" cy="262" r="92" fill="url(#halo)" />
    <ellipse cx="256" cy="318" rx="58" ry="14" fill="#000" opacity=".45" />
    <path d="M256 168a32 32 0 0 1 20 57c14 9 22 26 22 46l14 41h-112l14-41c0-20 8-37 22-46a32 32 0 0 1 20-57z"
      fill="url(#pawn)" stroke="#e9fffa" stroke-opacity=".8" stroke-width="3" />
    <rect x="194" y="306" width="124" height="20" rx="10" fill="url(#pawn)" stroke="#e9fffa" stroke-opacity=".8" stroke-width="3" />
    <path d="M236 192a14 14 0 0 1 14-10" stroke="#fff" stroke-width="6" stroke-linecap="round" fill="none" opacity=".75" />
  </g>
</svg>`;
}

const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined, args: ['--no-sandbox'] });
const jobs = [
  { file: '192.png', size: 192, art: { scale: 1, round: true } },
  { file: '512.png', size: 512, art: { scale: 1, round: true } },
  { file: 'maskable.png', size: 512, art: { scale: .78, round: false } },   // full bleed, art inside the safe zone
];
for (const j of jobs) {
  const page = await browser.newPage({ viewport: { width: j.size, height: j.size } });
  await page.setContent(`<!doctype html><style>html,body{margin:0;background:transparent}svg{display:block;width:${j.size}px;height:${j.size}px}</style>${svg(j.art)}`);
  await page.screenshot({ path: path.join(outDir, j.file), omitBackground: true });
  await page.close();
  console.log('wrote', path.join(outDir, j.file));
}
await browser.close();
