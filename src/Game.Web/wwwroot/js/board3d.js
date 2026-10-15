import * as THREE from './vendor/three.module.min.js';
import { createWorld } from './3d/scene.js';
import { createCamera } from './3d/camera.js';

let renderer = null, world = null, cam = null, raf = 0, canvas = null;
let dotnet = null, hover = -1, ray = null, pointer = null;

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

function loop() {
  if (!renderer) return;
  raf = requestAnimationFrame(loop);
  world.step();
  renderer.render(world.scene, cam.camera);
}

export function init(canvasEl, boardJson, dotnetRef) {
  dispose();
  dotnet = dotnetRef || null;
  try {
    canvas = canvasEl;
    const board = JSON.parse(boardJson);
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    world = createWorld(board);
    ray = new THREE.Raycaster(); pointer = new THREE.Vector2();
    cam = createCamera(canvas, world.side, {
      pick, setHover,
      click: i => { if (dotnet) dotnet.invokeMethodAsync('TileClicked', i).catch(() => {}); },
    });
    resize();
    window.addEventListener('resize', resize);
    loop();
    return true;
  } catch (e) {
    console.warn('board3d init failed', e);
    dispose();
    return false;
  }
}

export function applyState(stateJson) { if (world) world.applyState(JSON.parse(stateJson)); }
export function playEvent(eventJson) { if (world) world.playEvent(JSON.parse(eventJson)); }

export function dispose() {
  if (raf) cancelAnimationFrame(raf);
  raf = 0;
  window.removeEventListener('resize', resize);
  if (cam) cam.dispose();
  if (world) world.dispose();
  if (renderer) { renderer.dispose(); renderer = null; }
  dotnet = null; hover = -1;
  world = null; cam = null; canvas = null;
}
