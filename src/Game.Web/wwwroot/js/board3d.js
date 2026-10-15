import * as THREE from './vendor/three.module.min.js';
import { createWorld } from './3d/scene.js';
import { createCamera } from './3d/camera.js';
import { createLook } from './3d/materials.js';

let renderer = null, world = null, cam = null, raf = 0, canvas = null, ro = null, last = 0;
let dotnet = null, hover = -1, ray = null, pointer = null, quality = 'high';

function applyQuality() {
  if (!renderer) return;
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, quality === 'low' ? 1 : 2));
  world.setQuality(quality);
  resize();
}

function resize() {
  if (!renderer || !cam) return;
  const [w, h] = cam.resize();
  renderer.setSize(w, h, false);
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
  world.step();
  renderer.render(world.scene, cam.camera);
}

function parse(j) { try { return j ? JSON.parse(j) : null; } catch { return null; } }

export function init(canvasEl, boardJson, dotnetRef, optsJson) {
  dispose();
  dotnet = dotnetRef || null;
  try {
    const opts = parse(optsJson) || {};
    quality = opts.quality === 'low' ? 'low' : 'high';
    canvas = canvasEl;
    const board = JSON.parse(boardJson);
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.0;
    const look = createLook(opts.theme);
    world = createWorld(board, look, renderer);
    world.setTheme(opts.theme);
    ray = new THREE.Raycaster(); pointer = new THREE.Vector2();
    cam = createCamera(canvas, world.side, {
      pick, setHover,
      click: i => { if (dotnet) dotnet.invokeMethodAsync('TileClicked', i).catch(() => {}); },
    }, { reduceMotion: opts.reduceMotion });
    applyQuality();
    window.addEventListener('resize', resize);
    window.addEventListener('orientationchange', resize);
    if (typeof ResizeObserver !== 'undefined') { ro = new ResizeObserver(resize); ro.observe(canvas); }
    last = 0;
    raf = requestAnimationFrame(loop);
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
}

export function setQuality(q) {
  quality = q === 'low' ? 'low' : 'high';
  applyQuality();
}

export function applyState(stateJson) { if (world) world.applyState(JSON.parse(stateJson)); }
export function playEvent(eventJson) { if (world) world.playEvent(JSON.parse(eventJson)); }

export function dispose() {
  if (raf) cancelAnimationFrame(raf);
  raf = 0;
  window.removeEventListener('resize', resize);
  window.removeEventListener('orientationchange', resize);
  if (ro) { ro.disconnect(); ro = null; }
  if (cam) cam.dispose();
  if (world) world.dispose();
  if (renderer) { renderer.dispose(); renderer = null; }
  dotnet = null; hover = -1;
  world = null; cam = null; canvas = null;
}
