// Self-contained test stack for the web checks: publishes the web client (unless given a wwwroot), starts a
// Game.Server on a free loopback port with a throwaway token secret, and serves the wwwroot from a second free
// loopback port that proxies /api, /healthz and WebSocket upgrades to the server. stop() kills both.
//
//   import { startStack, browserArgs } from './harness.mjs';
//   const stack = await startStack({ wwwroot });   // or {} to publish into a temp dir first
//   ... stack.base (http://127.0.0.1:<port>/), stack.apiPort ...
//   await stack.stop();
//
// Run standalone to keep a stack up for manual poking:  node tools/web-smoke/harness.mjs [--serve=<wwwroot>]
import http from 'node:http';
import net from 'node:net';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

/** Chromium flags for WebGL on a GPU-less host: Mesa llvmpipe through EGL on arm64 (SwiftShader crashes there). */
export function browserArgs(gl = process.arch === 'arm64' ? 'egl' : 'swiftshader') {
  const GL = gl === 'egl' ? ['--use-gl=angle', '--use-angle=gl-egl']
    : ['--use-gl=angle', '--use-angle=swiftshader-webgl', '--enable-unsafe-swiftshader'];
  return [...GL, '--no-sandbox', '--disable-gpu-sandbox', '--ignore-gpu-blocklist'];
}

export function freePort() {
  return new Promise((res, rej) => {
    const s = net.createServer();
    s.once('error', rej);
    s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); });
  });
}

const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.wasm': 'application/wasm', '.png': 'image/png', '.svg': 'image/svg+xml', '.webmanifest': 'application/manifest+json', '.dat': 'application/octet-stream' };

/** Static file server for a published wwwroot (SPA fallback to index.html) that proxies the API and WebSockets. */
export function serveStatic(root, apiPort, port) {
  root = path.resolve(root);
  const server = http.createServer((req, res) => {
    if (req.url.startsWith('/api') || req.url.startsWith('/healthz')) {
      const p = http.request({ host: '127.0.0.1', port: apiPort, path: req.url, method: req.method, headers: req.headers },
        r => { res.writeHead(r.statusCode, r.headers); r.pipe(res); });
      p.on('error', () => { res.writeHead(502); res.end(); });
      req.pipe(p); return;
    }
    let f = path.join(root, decodeURIComponent(req.url.split('?')[0]));
    if (!f.startsWith(root)) { res.writeHead(403); return res.end(); }
    if (!fs.existsSync(f) || fs.statSync(f).isDirectory()) f = path.join(root, 'index.html');
    res.writeHead(200, { 'content-type': TYPES[path.extname(f)] || 'application/octet-stream', 'cache-control': 'no-store' });
    fs.createReadStream(f).pipe(res);
  });
  server.on('upgrade', (req, sock, head) => {
    const up = net.connect(apiPort, '127.0.0.1', () => {
      up.write(`${req.method} ${req.url} HTTP/1.1\r\n` + Object.entries(req.headers).map(([k, v]) => `${k}: ${v}`).join('\r\n') + '\r\n\r\n');
      up.write(head); sock.pipe(up); up.pipe(sock);
    });
    up.on('error', () => sock.destroy()); sock.on('error', () => up.destroy());
  });
  return new Promise(r => server.listen(port, '127.0.0.1', () => r(server)));
}

function run(cmd, args, opts = {}) {
  const r = spawnSync(cmd, args, { cwd: repo, stdio: opts.quiet ? 'pipe' : 'inherit', encoding: 'utf8', ...opts });
  if (r.status !== 0) throw new Error(`${cmd} ${args.join(' ')} failed (${r.status})\n${r.stdout || ''}${r.stderr || ''}`);
  return r;
}

async function waitHealthy(port, ms = 60000) {
  const until = Date.now() + ms;
  while (Date.now() < until) {
    const ok = await new Promise(res => http.get({ host: '127.0.0.1', port, path: '/healthz' }, r => { r.resume(); res(r.statusCode === 200); }).on('error', () => res(false)));
    if (ok) return;
    await new Promise(r => setTimeout(r, 300));
  }
  throw new Error(`Game.Server on :${port} did not become healthy`);
}

/**
 * opts: { wwwroot?: published wwwroot (default: dotnet publish into a temp dir), log?: server log file,
 *         build?: false to skip building Game.Server (default true), botDelay?: seconds (default 0.3) }
 */
export async function startStack(opts = {}) {
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'be-smoke-'));
  let wwwroot = opts.wwwroot;
  if (!wwwroot) {
    console.log('[harness] dotnet publish src/Game.Web -c Release');
    run('dotnet', ['publish', 'src/Game.Web', '-c', 'Release', '-o', path.join(tmp, 'pub'), '--nologo', '-v', 'q'], { quiet: true });
    wwwroot = path.join(tmp, 'pub', 'wwwroot');
  }
  if (opts.build !== false) {
    console.log('[harness] dotnet build src/Game.Server -c Release');
    run('dotnet', ['build', 'src/Game.Server', '-c', 'Release', '--nologo', '-v', 'q'], { quiet: true });
  }
  const dll = path.join(repo, 'src/Game.Server/bin/Release/net8.0/Game.Server.dll');
  const apiPort = await freePort(), webPort = await freePort();
  const log = fs.openSync(opts.log || path.join(tmp, 'server.log'), 'a');
  const server = spawn('dotnet', [dll], {
    cwd: tmp, stdio: ['ignore', log, log],
    env: { ...process.env, ASPNETCORE_URLS: `http://127.0.0.1:${apiPort}`,
      BoardEmpire__TokenSecret: crypto.randomBytes(32).toString('base64'), BoardEmpire__BotDelaySeconds: String(opts.botDelay ?? 0.3) },
  });
  let exited = false;
  server.on('exit', () => { exited = true; });
  try { await waitHealthy(apiPort); } catch (e) { server.kill('SIGTERM'); throw e; }
  const web = await serveStatic(wwwroot, apiPort, webPort);
  const base = `http://127.0.0.1:${webPort}/`;
  console.log(`[harness] Game.Server 127.0.0.1:${apiPort} (pid ${server.pid}), web ${base}, wwwroot ${wwwroot}`);
  return {
    base, apiPort, webPort, wwwroot, pid: server.pid,
    async stop() {
      await new Promise(r => web.close(() => r()));
      if (!exited) {
        server.kill('SIGTERM');
        await new Promise(r => { const t = setTimeout(() => { try { server.kill('SIGKILL'); } catch { } r(); }, 5000); server.once('exit', () => { clearTimeout(t); r(); }); });
      }
      fs.rmSync(path.join(tmp, 'pub'), { recursive: true, force: true });
    },
  };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const a = process.argv.slice(2).find(x => x.startsWith('--serve='));
  const stack = await startStack({ wwwroot: a ? a.slice(8) : undefined });
  console.log('Stack up. Ctrl+C to stop.');
  const stop = async () => { await stack.stop(); process.exit(0); };
  process.on('SIGINT', stop); process.on('SIGTERM', stop);
}
