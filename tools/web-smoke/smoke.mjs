import { chromium } from 'playwright';

const base = process.argv[2] || 'https://monopoly.algosculptor.com/';
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined, args: ['--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const page = await browser.newPage();
const errors = [];
page.on('pageerror', e => errors.push(String(e)));
page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });

await page.goto(base, { waitUntil: 'networkidle' });
await page.fill('input[placeholder="Guest"]', 'SmokeBot');
await page.click('text=Create room');
await page.waitForURL(/\/room\//, { timeout: 60000 });
await page.click('text=Add bot');
await page.click('text=Ready');
await page.click('text=Start match');
await page.waitForURL(/\/play\//, { timeout: 60000 });
await page.waitForSelector('.board2d', { timeout: 60000 });
await page.click('text=Roll dice');
await page.waitForTimeout(3000);
await page.click('button:has-text("3D")');
await page.waitForSelector('canvas.board3d', { timeout: 10000 });
await page.waitForTimeout(1500);

if (errors.length) {
  console.error('Browser errors:\n' + errors.join('\n'));
  await browser.close();
  process.exit(1);
}
console.log('smoke ok');
await browser.close();
