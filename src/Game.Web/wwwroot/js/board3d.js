import * as THREE from './vendor/three.module.min.js';
import { createWorld } from './3d/scene.js';
import { createCamera } from './3d/camera.js';
import { createLook } from './3d/materials.js';
import { createFx, probeQuality } from './fx.js';

let renderer = null, world = null, cam = null, raf = 0, canvas = null, ro = null, last = 0;
let dotnet = null, hover = -1, ray = null, pointer = null, quality = 'high';
let fx = null, reduce = false, auto = false, probeGen = 0, curTheme = null;

function autoDefault() {
  const small = typeof matchMedia === 'function' && matchMedia('(max-width:700px)').matches;
  const cores = navigator.hardwareConcurrency || 8;
  return small || cores <= 4 ? 'low' : 'high';
}

function runProbe() {
  const g = ++probeGen;
  setTimeout(() => {
    if (g !== probeGen || !renderer || !auto) return;
    probeQuality(() => {}, 20).then(q => {
      if (g !== probeGen || !renderer || !auto) return;
      if (q === 'low' && quality !== 'low') { quality = 'low'; applyQuality(); }
    });
  }, 1500);
}

function applyQuality() {
  if (!renderer) return;
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, quality === 'low' ? 1 : 2));
  world.setQuality(quality);
  if (fx) fx.setQuality(quality);
  resize();
}

function resize() {
  if (!renderer || !cam) return;
  const [w, h] = cam.resize();
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
  cam.tick(dt);
  world.step(dt);
  if (fx) fx.render(dt); else renderer.render(world.scene, cam.camera);
}

function parse(j) { try { return j ? JSON.parse(j) : null; } catch { return null; } }

export function init(canvasEl, boardJson, dotnetRef, optsJson) {
  dispose();
  dotnet = dotnetRef || null;
  try {
    const opts = parse(optsJson) || {};
    auto = opts.quality !== 'low' && opts.quality !== 'high';
    quality = auto ? autoDefault() : opts.quality;
    reduce = !!opts.reduceMotion;
    curTheme = opts.theme || null;
    canvas = canvasEl;
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
    window.addEventListener('resize', resize);
    window.addEventListener('orientationchange', resize);
    if (typeof ResizeObserver !== 'undefined') { ro = new ResizeObserver(resize); ro.observe(canvas); }
    last = 0;
    raf = requestAnimationFrame(loop);
    if (auto && quality === 'high') runProbe();
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
  if (auto && quality === 'high') runProbe(); else probeGen++;
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

export function dispose() {
  probeGen++;
  if (raf) cancelAnimationFrame(raf);
  raf = 0;
  window.removeEventListener('resize', resize);
  window.removeEventListener('orientationchange', resize);
  if (ro) { ro.disconnect(); ro = null; }
  if (cam) cam.dispose();
  if (fx) { fx.dispose(); fx = null; }
  if (world) world.dispose();
  if (renderer) {
    // The canvas keeps its GL context across init() calls; reset the unpack state the last
    // CanvasTexture upload left set, or the next renderer's empty 3D texture upload fails.
    const gl = renderer.getContext();
    if (gl && !gl.isContextLost()) { gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false); gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false); }
    renderer.dispose(); renderer = null;
  }
  dotnet = null; hover = -1;
  world = null; cam = null; canvas = null;
}
