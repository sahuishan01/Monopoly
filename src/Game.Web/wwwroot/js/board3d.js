import * as THREE from './vendor/three.module.min.js';
import { createWorld } from './3d/scene.js';
import { createCamera } from './3d/camera.js';
import { createLook } from './3d/materials.js';
import { createFx, probeQuality } from './fx.js';
import { autoTier, NARROW_PX } from './hero-policy.js';

let renderer = null, world = null, cam = null, raf = 0, canvas = null, ro = null, last = 0;
let dotnet = null, hover = -1, ray = null, pointer = null, quality = 'high';
let fx = null, reduce = false, auto = false, probeGen = 0, curTheme = null, fogAt = NaN;

function autoDefault() {
  const small = typeof matchMedia === 'function' && matchMedia(`(max-width:${NARROW_PX}px)`).matches;
  return autoTier({ width: small ? NARROW_PX : NARROW_PX + 1, cores: navigator.hardwareConcurrency || 8 });
}

// Auto mode: downgrade after one slow probe; upgrade back to high only after
// two consecutive good probes (and only if the device default is high).
let good = 0, probeTimer = 0, resizeTimer = 0;
function runProbe(delay = 1500) {
  const g = ++probeGen;
  clearTimeout(probeTimer);
  probeTimer = setTimeout(() => {
    if (g !== probeGen || !renderer || !auto) return;
    probeQuality(() => {}, 20).then(q => {
      if (g !== probeGen || !renderer || !auto) return;
      if (q === 'low') { good = 0; if (quality !== 'low') { quality = 'low'; applyQuality(); } }
      else if (quality === 'low' && autoDefault() === 'high') {
        if (++good >= 2) { good = 0; quality = 'high'; applyQuality(); } else runProbe(1000);
      }
    });
  }, delay);
}

function onResize() { resize(); onViewportChange(); }

function onViewportChange() {
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => {
    if (!renderer || !auto) return;
    good = 0;
    if (autoDefault() === 'low' && quality !== 'low') { quality = 'low'; applyQuality(); }
    else runProbe(0);
  }, 1000);
}

function applyQuality() {
  if (!renderer) return;
  world.setQuality(quality);
  if (fx) fx.setQuality(quality);
  resize();
}

// Low tier caps the backing store to bound fill cost. On a high-DPR screen with more than 4 cores it allows
// 1.5x (1x is visibly blurry on phones); weaker devices stay at 1x, the tier's cost cap.
function lowDpr() {
  const dpr = window.devicePixelRatio || 1, cores = navigator.hardwareConcurrency || 4;
  return cores > 4 && dpr >= 1.5 ? 1.5 : 1;
}

function resize() {
  if (!renderer || !cam) return;
  const [w, h] = cam.resize();
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, quality === 'low' ? lowDpr() : 2));
  renderer.setSize(w, h, false);
  if (fx) fx.resize(w, h);
}

function pick(ev) {
  const r = canvas.getBoundingClientRect();
  pointer.set(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
  ray.setFromCamera(pointer, cam.camera);
  const hit = ray.intersectObjects(world.pickables(), false)[0];
  return hit ? world.tiles.findIndex(t => t.mesh === hit.object) : -1;
}

function setHover(i) {
  if (i === hover) return;
  if (hover >= 0 && world.tiles[hover]) world.tiles[hover].mesh.material.emissive.setHex(0x000000);
  hover = i;
  if (i >= 0) world.tiles[i].mesh.material.emissive.setHex(0x333333);
  canvas.style.cursor = i >= 0 ? 'pointer' : 'grab';
}

function loop(now) {
  if (!renderer) return;
  raf = requestAnimationFrame(loop);
  const dt = last ? (now - last) / 1000 : 0.016;
  last = now;
  try {
    cam.tick(dt);
    // Fog tracks the camera distance, so a far-fitted camera (portrait phones) does not wash out the board.
    const fog = world.scene.fog;
    if (fog && cam.distance !== fogAt) { fogAt = cam.distance; fog.near = fogAt + world.side * 1.2; fog.far = fogAt + world.side * 5.9; }
    world.step(dt);
    if (fx) fx.render(dt); else renderer.render(world.scene, cam.camera);
  } catch (e) { if (!loop.warned) { loop.warned = true; console.warn('board3d frame failed', e); } }
}

// The GPU dropped the context (driver reset, too many contexts, tab in the background on mobile): stop drawing and
// tell the page, which falls back to the 2D board. Picking 3D again re-inits on a fresh canvas.
function onContextLost(e) {
  if (e && e.preventDefault) e.preventDefault();
  if (raf) cancelAnimationFrame(raf);
  raf = 0;
  const d = dotnet;
  if (d) d.invokeMethodAsync('ContextLost').catch(() => {});
}

function parse(j) { try { return j ? JSON.parse(j) : null; } catch { return null; } }

export function init(canvasEl, boardJson, dotnetRef, optsJson) {
  dispose();
  loop.warned = false;
  fogAt = NaN;
  dotnet = dotnetRef || null;
  try {
    const opts = parse(optsJson) || {};
    auto = opts.quality !== 'low' && opts.quality !== 'high';
    quality = auto ? autoDefault() : opts.quality;
    reduce = !!opts.reduceMotion;
    curTheme = opts.theme || null;
    canvas = canvasEl;
    canvas.addEventListener('webglcontextlost', onContextLost);
    const board = JSON.parse(boardJson);
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.0;
    const look = createLook(opts.theme);
    world = createWorld(board, look, renderer, { reduceMotion: opts.reduceMotion });
    world.setTheme(opts.theme);
    ray = new THREE.Raycaster(); pointer = new THREE.Vector2();
    cam = createCamera(canvas, world.side, {
      pick, setHover,
      click: i => { if (dotnet) dotnet.invokeMethodAsync('TileClicked', i).catch(() => {}); },
    }, { reduceMotion: opts.reduceMotion });
    fx = createFx(THREE, world.scene, renderer, cam.camera, curTheme, quality);
    applyQuality();
    window.addEventListener('resize', onResize);
    window.addEventListener('orientationchange', onResize);
    if (typeof ResizeObserver !== 'undefined') { ro = new ResizeObserver(onResize); ro.observe(canvas); }
    last = 0;
    raf = requestAnimationFrame(loop);
    if (auto && autoDefault() === 'high') runProbe();
    return true;
  } catch (e) {
    console.warn('board3d init failed', e);
    dispose();
    return false;
  }
}

export function setTheme(themeJson) {
  if (!world) return;
  const t = typeof themeJson === 'string' ? parse(themeJson) : themeJson;
  world.setTheme(t);
  curTheme = t;
  if (fx) fx.setTheme(t);
}

export function setQuality(q) {
  auto = q !== 'low' && q !== 'high';
  quality = auto ? autoDefault() : q;
  applyQuality();
  good = 0;
  if (auto && autoDefault() === 'high') runProbe(); else { probeGen++; clearTimeout(probeTimer); }
}

export function setReduceMotion(v) { reduce = !!v; if (cam) cam.setReduceMotion(v); if (world) world.setReduceMotion(v); }

export function applyState(stateJson) { if (world) world.applyState(JSON.parse(stateJson)); }
export function playEvent(eventJson, burst) {
  if (!world) return;
  const e = typeof eventJson === 'string' ? parse(eventJson) : eventJson;
  if (!e) return;
  world.playEvent(e, !!burst);
  if (!burst && !reduce && fx) particles(e);
  if (!cam) return;
  const t = e['$t'];
  if (t === 'TurnStarted') { const p = world.tokenPos(e.Player); if (p) cam.follow(p); }
  else if (t === 'AuctionStarted' || (typeof t === 'string' && t.startsWith('Trade'))) cam.overview();
}

/** Test hook: NDC bounds of the board's bounding box as the camera currently sees it (|v| <= 1 is on screen). */
function framing() {
  if (!cam) return null;
  const { half, top } = cam.fit, v = new THREE.Vector3(), b = { minX: 1e9, maxX: -1e9, minY: 1e9, maxY: -1e9 };
  cam.camera.updateMatrixWorld();
  for (const x of [-half, half]) for (const y of [0, top]) for (const z of [-half, half]) {
    v.set(x, y, z).project(cam.camera);
    b.minX = Math.min(b.minX, v.x); b.maxX = Math.max(b.maxX, v.x); b.minY = Math.min(b.minY, v.y); b.maxY = Math.max(b.maxY, v.y);
  }
  return { ...b, aspect: cam.camera.aspect, distance: cam.distance, fit: cam.fit.radius };
}

/** Test hook: the tile under a viewport point (what a tap there would select), or -1. */
function pickAt(x, y) { return canvas && world ? pick({ clientX: x, clientY: y }) : -1; }

/** Test hook: NDC position of tile i's centre as the camera currently sees it, or null. */
function tileNdc(i) {
  const p = world && cam ? tilePos(i) : null;
  if (!p) return null;
  cam.camera.updateMatrixWorld();
  const v = p.clone().project(cam.camera);
  return { x: v.x, y: v.y };
}

/** Test-only hooks (render-check.mjs, mobile-check.mjs). Not part of the app API. */
export const __test = {
  framing, tileNdc, pickAt,
  /** True once the camera has eased onto its goal (nothing will move until the next auto/user change). */
  settled: () => !!cam && cam.settled,
};

function tilePos(i) { const t = world.tiles[i]; return t ? t.mesh.position : null; }
function particles(e) {
  const t = e['$t'];
  if (t === 'RentPaid' || t === 'MoneyTransferred') {
    const a = world.tokenPos(t === 'RentPaid' ? e.Payer : e.From), b = world.tokenPos(t === 'RentPaid' ? e.Payee : e.To);
    if (a && b) fx.trail(a, b, 'trail');
    else if (a || b) fx.burst(a || b, 'cash');
  } else if (t === 'PropertyPurchased' || t === 'BuildingConstructed') {
    const p = tilePos(e.Tile);
    if (p) fx.burst(p, 'build');
  } else if (t === 'GameEnded') {
    fx.burst({ x: 0, y: 0, z: 0 }, 'win');
  }
}

/** loseContext: also release the GL context now (the component is going away and its canvas with it). */
export function dispose(loseContext) {
  probeGen++; clearTimeout(probeTimer); clearTimeout(resizeTimer);
  if (raf) cancelAnimationFrame(raf);
  raf = 0;
  window.removeEventListener('resize', onResize);
  window.removeEventListener('orientationchange', onResize);
  if (ro) { ro.disconnect(); ro = null; }
  if (canvas) canvas.removeEventListener('webglcontextlost', onContextLost);   // before forceContextLoss below
  if (cam) cam.dispose();
  if (fx) { fx.dispose(); fx = null; }
  if (world) world.dispose();
  if (renderer) {
    // The canvas keeps its GL context across init() calls; reset the unpack state the last
    // CanvasTexture upload left set, or the next renderer's empty 3D texture upload fails.
    const gl = renderer.getContext();
    if (gl && !gl.isContextLost()) { gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false); gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false); }
    renderer.dispose();
    if (loseContext === true && gl && !gl.isContextLost()) { try { renderer.forceContextLoss(); } catch { /* ignore */ } }
    renderer = null;
  }
  dotnet = null; hover = -1;
  world = null; cam = null; canvas = null;
}
