import * as THREE from './vendor/three.module.min.js';

let renderer = null, scene = null, camera = null, raf = 0, canvas = null, board = null;
const tokens = new Map();
const tiles = [];
let side = 10;

function cell(tile) {
  const n = side * 4;
  const i = ((tile % n) + n) % n;
  if (i < side) return [side, side - i];
  if (i < side * 2) return [side - (i - side), 0];
  if (i < side * 3) return [0, i - side * 2];
  return [i - side * 3, side];
}

function pos(tile) {
  const [r, c] = cell(tile);
  return new THREE.Vector3((c - side / 2) * 1.2, 0, (r - side / 2) * 1.2);
}

function disposeMesh(m) {
  if (!m) return;
  if (scene) scene.remove(m);
  if (m.geometry) m.geometry.dispose();
  if (m.material) m.material.dispose();
}

function resize() {
  if (!renderer || !canvas) return;
  const w = canvas.clientWidth || 600;
  const h = canvas.clientHeight || 600;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}

function loop() {
  if (!renderer) return;
  raf = requestAnimationFrame(loop);
  for (const t of tokens.values()) t.mesh.position.lerp(t.target, 0.15);
  renderer.render(scene, camera);
}

export function init(canvasEl, boardJson) {
  dispose();
  try {
    canvas = canvasEl;
    board = JSON.parse(boardJson);
    side = board.Tiles.length / 4;
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    scene = new THREE.Scene();
    scene.background = new THREE.Color(0x0b0d12);
    camera = new THREE.PerspectiveCamera(45, 1, 0.1, 100);
    camera.position.set(0, side * 1.5, side * 1.3);
    camera.lookAt(0, 0, 0);
    scene.add(new THREE.AmbientLight(0xffffff, 0.7));
    const sun = new THREE.DirectionalLight(0xffffff, 0.8);
    sun.position.set(5, 10, 6);
    scene.add(sun);

    const colors = {};
    for (const d of board.Districts || []) colors[d.Id] = d.Color;
    board.Tiles.forEach((t, i) => {
      const color = new THREE.Color(colors[t.District] || '#2a3344');
      const mesh = new THREE.Mesh(
        new THREE.BoxGeometry(1.1, 0.12, 1.1),
        new THREE.MeshStandardMaterial({ color }));
      mesh.position.copy(pos(i));
      scene.add(mesh);
      tiles.push({ mesh, building: null });
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

function ensureToken(p) {
  let t = tokens.get(p.Id);
  if (!t) {
    const mesh = new THREE.Mesh(
      new THREE.CylinderGeometry(0.22, 0.22, 0.5, 16),
      new THREE.MeshStandardMaterial({ color: new THREE.Color().setHSL((p.Id * 0.17) % 1, 0.7, 0.55) }));
    scene.add(mesh);
    t = { mesh, target: pos(p.Position).setY(0.35) };
    mesh.position.copy(t.target);
    tokens.set(p.Id, t);
  }
  return t;
}

export function applyState(stateJson) {
  if (!scene) return;
  const s = JSON.parse(stateJson);
  for (const p of s.Players) {
    const t = ensureToken(p);
    t.mesh.visible = !p.Bankrupt;
    t.target = pos(p.Position).setY(0.35);
  }
  s.Properties.forEach((prop, i) => {
    if (!prop || !tiles[i]) return;
    const tile = tiles[i];
    const h = 0.12 + prop.Level * 0.25;
    if (tile.building) { disposeMesh(tile.building); tile.building = null; }
    if (prop.Level > 0 || prop.Owner >= 0) {
      const b = new THREE.Mesh(
        new THREE.BoxGeometry(0.6, Math.max(h, 0.2), 0.6),
        new THREE.MeshStandardMaterial({
          color: prop.Owner >= 0 ? new THREE.Color().setHSL((prop.Owner * 0.17) % 1, 0.6, 0.5) : 0x555555,
          transparent: prop.Mortgaged, opacity: prop.Mortgaged ? 0.4 : 1 }));
      b.position.copy(pos(i)).setY(h / 2 + 0.06);
      scene.add(b);
      tile.building = b;
    }
  });
}

export function playEvent(eventJson) {
  if (!scene) return;
  const e = JSON.parse(eventJson);
  if (e['$t'] === 'PlayerMoved') {
    const t = tokens.get(e.Player);
    if (t) t.target = pos(e.To).setY(0.35);
  }
}

export function dispose() {
  if (raf) cancelAnimationFrame(raf);
  raf = 0;
  window.removeEventListener('resize', resize);
  for (const t of tokens.values()) disposeMesh(t.mesh);
  tokens.clear();
  for (const t of tiles) { disposeMesh(t.building); disposeMesh(t.mesh); }
  tiles.length = 0;
  if (renderer) { renderer.dispose(); renderer = null; }
  scene = null; camera = null; canvas = null;
}
