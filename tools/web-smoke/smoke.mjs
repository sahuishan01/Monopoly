// End-to-end smoke: guest login, create a room, add a bot, ready, start, take a turn, switch 2D <-> 3D.
//   node tools/web-smoke/smoke.mjs [baseUrl] [--gl=egl|swiftshader]
// WebGL uses --gl=egl by default on arm64 (the SwiftShader bundled with Chromium crashes there; see
// render-check.mjs) and swiftshader elsewhere. Set CHROMIUM_PATH to use another Chromium build.
import { chromium } from 'playwright';

const args = process.argv.slice(2);
const base = args.find(a => !a.startsWith('--')) || 'https://monopoly.algosculptor.com/';
const gl = (args.find(a => a.startsWith('--gl=')) || '').slice(5) || (process.arch === 'arm64' ? 'egl' : 'swiftshader');
const GL = gl === 'egl' ? ['--use-gl=angle', '--use-angle=gl-egl']
  : ['--use-gl=angle', '--use-angle=swiftshader-webgl', '--enable-unsafe-swiftshader'];
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined,
  args: [...GL, '--no-sandbox', '--disable-gpu-sandbox', '--ignore-gpu-blocklist'] });
const page = await browser.newPage();
const errors = [];
page.on('pageerror', e => errors.push(String(e)));
page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
const step = s => console.log('- ' + s);

await page.goto(base, { waitUntil: 'networkidle' });
await page.fill('input[placeholder="Guest"]', 'SmokeBot');
await page.getByRole('button', { name: 'Create room' }).click();
await page.waitForURL(/\/room\//, { timeout: 60000 });
step('room created: ' + page.url());
await page.getByRole('button', { name: 'Add bot' }).first().click();
await page.waitForSelector('.seats .seat.bot', { timeout: 15000 });
step('bot added');
await page.getByRole('button', { name: 'Ready', exact: true }).click();
await page.getByRole('button', { name: 'Start match' }).click();
await page.waitForURL(/\/play\//, { timeout: 60000 });
await page.waitForSelector('.board2d, canvas.board3d', { timeout: 60000 });
step('match started');
// Roll once it is our turn and the HUD has settled (the button is disabled while events animate).
const roll = page.locator('.hud button:has-text("Roll dice"):not([disabled])').first();
await roll.waitFor({ timeout: 60000 });
await roll.click();
await page.waitForFunction(() => ![...document.querySelectorAll('.hud button')].some(b => /Roll dice/.test(b.textContent)), null, { timeout: 30000 });
step('rolled');
await page.waitForTimeout(2000);
for (let i = 0; i < 3 && await page.locator('[aria-modal="true"]').count(); i++) { await page.keyboard.press('Escape'); await page.waitForTimeout(300); }
await page.getByRole('radio', { name: '2D' }).first().click();
await page.waitForSelector('.board2d', { timeout: 10000 });
await page.getByRole('radio', { name: '3D' }).first().click();
await page.waitForSelector('canvas.board3d', { timeout: 10000 });
await page.waitForTimeout(1500);
step('switched 2D -> 3D');

if (errors.length) {
  console.error('Browser errors:\n' + errors.join('\n'));
  await browser.close();
  process.exit(1);
}
console.log('smoke ok');
await browser.close();
