import * as THREE from './vendor/three.module.min.js';

let renderer = null, scene = null, camera = null, raf = 0, canvas = null, board = null;
const tokens = new Map();
const tiles = [];
let side = 10;
let dotnet = null, hover = -1;
const orbit = { theta: 0, phi: 0.9, radius: 16, target: null };
let ray = null, pointer = null, down = null, cleanup = [];

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

function updateCamera() {
  const o = orbit;
  camera.position.set(
    o.target.x + o.radius * Math.sin(o.phi) * Math.sin(o.theta),
    o.target.y + o.radius * Math.cos(o.phi),
    o.target.z + o.radius * Math.sin(o.phi) * Math.cos(o.theta));
  camera.lookAt(o.target);
}

function label(text) {
  const c = document.createElement('canvas');
  c.width = 256; c.height = 256;
  const g = c.getContext('2d');
  g.fillStyle = '#fff'; g.font = 'bold 34px sans-serif'; g.textAlign = 'center'; g.textBaseline = 'middle';
  const words = text.split(' ');
  const lines = [];
  let cur = '';
  for (const w of words) { if ((cur + ' ' + w).trim().length > 11 && cur) { lines.push(cur); cur = w; } else cur = (cur + ' ' + w).trim(); }
  if (cur) lines.push(cur);
  lines.slice(0, 3).forEach((l, i, a) => g.fillText(l, 128, 128 + (i - (a.length - 1) / 2) * 42));
  const tex = new THREE.CanvasTexture(c);
  const m = new THREE.Mesh(new THREE.PlaneGeometry(1.0, 1.0),
    new THREE.MeshBasicMaterial({ map: tex, transparent: true }));
  m.rotation.x = -Math.PI / 2;
  return m;
}

function pick(ev) {
  const r = canvas.getBoundingClientRect();
  pointer.set(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
  ray.setFromCamera(pointer, camera);
  const hit = ray.intersectObjects(tiles.map(t => t.mesh), false)[0];
  return hit ? tiles.findIndex(t => t.mesh === hit.object) : -1;
}

function setHover(i) {
  if (i === hover) return;
  if (hover >= 0 && tiles[hover]) tiles[hover].mesh.material.emissive.setHex(0x000000);
  hover = i;
  if (i >= 0) tiles[i].mesh.material.emissive.setHex(0x333333);
  canvas.style.cursor = i >= 0 ? 'pointer' : 'grab';
}

function bindControls() {
  const pts = new Map();
  let lastDist = 0;
  const on = (t, e, f, o) => { canvas.addEventListener(e, f, o); cleanup.push(() => canvas.removeEventListener(e, f, o)); };
  on(canvas, 'contextmenu', e => e.preventDefault());
  on(canvas, 'pointerdown', e => {
    canvas.setPointerCapture(e.pointerId);
    pts.set(e.pointerId, { x: e.clientX, y: e.clientY, btn: e.button });
    down = { x: e.clientX, y: e.clientY, moved: false };
    lastDist = 0;
  });
  on(canvas, 'pointermove', e => {
    const p = pts.get(e.pointerId);
    if (!p) { setHover(pick(e)); return; }
    const dx = e.clientX - p.x, dy = e.clientY - p.y;
    if (down && Math.hypot(e.clientX - down.x, e.clientY - down.y) > 4) down.moved = true;
    if (pts.size === 2) {
      const [a, b] = [...pts.values()];
      pts.set(e.pointerId, { ...p, x: e.clientX, y: e.clientY });
      const [c, d] = [...pts.values()];
      const dist = Math.hypot(c.x - d.x, c.y - d.y);
      if (lastDist) orbit.radius = Math.min(40, Math.max(5, orbit.radius * lastDist / dist));
      lastDist = dist;
    } else if (p.btn === 2 || e.shiftKey) {
      const k = orbit.radius * 0.0018;
      orbit.target.x -= (dx * Math.cos(orbit.theta) + dy * Math.sin(orbit.theta)) * k;
      orbit.target.z -= (-dx * Math.sin(orbit.theta) + dy * Math.cos(orbit.theta)) * k;
      pts.set(e.pointerId, { ...p, x: e.clientX, y: e.clientY });
    } else {
      orbit.theta -= dx * 0.008;
      orbit.phi = Math.min(1.45, Math.max(0.15, orbit.phi - dy * 0.008));
      pts.set(e.pointerId, { ...p, x: e.clientX, y: e.clientY });
    }
    updateCamera();
  });
  const up = e => {
    pts.delete(e.pointerId);
    if (e.type === 'pointerup' && down && !down.moved && dotnet) {
      const i = pick(e);
      if (i >= 0) dotnet.invokeMethodAsync('TileClicked', i).catch(() => {});
    }
    down = null;
  };
  on(canvas, 'pointerup', up);
  on(canvas, 'pointercancel', up);
  on(canvas, 'pointerleave', () => setHover(-1));
  on(canvas, 'wheel', e => {
    e.preventDefault();
    orbit.radius = Math.min(40, Math.max(5, orbit.radius * (1 + Math.sign(e.deltaY) * 0.1)));
    updateCamera();
  }, { passive: false });
  on(canvas, 'dblclick', () => { orbit.theta = 0; orbit.phi = 0.9; orbit.radius = side * 1.6; orbit.target.set(0, 0, 0); updateCamera(); });
}

function loop() {
  if (!renderer) return;
  raf = requestAnimationFrame(loop);
  for (const t of tokens.values()) t.mesh.position.lerp(t.target, 0.15);
  renderer.render(scene, camera);
}

export function init(canvasEl, boardJson, dotnetRef) {
  dispose();
  dotnet = dotnetRef || null;
  try {
    canvas = canvasEl;
    board = JSON.parse(boardJson);
    side = board.Tiles.length / 4;
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    scene = new THREE.Scene();
    scene.background = new THREE.Color(0x0b0d12);
    camera = new THREE.PerspectiveCamera(45, 1, 0.1, 100);
    orbit.target = new THREE.Vector3(0, 0, 0);
    orbit.theta = 0; orbit.phi = 0.9; orbit.radius = side * 1.6;
    ray = new THREE.Raycaster(); pointer = new THREE.Vector2();
    updateCamera();
    bindControls();
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
      const lab = label(t.Name || '');
      lab.position.copy(pos(i)).setY(0.075);
      scene.add(lab);
      mesh.userData.label = lab;
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
  for (const t of tiles) { disposeMesh(t.building); disposeMesh(t.mesh.userData.label); disposeMesh(t.mesh); }
  tiles.length = 0;
  if (renderer) { renderer.dispose(); renderer = null; }
  for (const f of cleanup) f();
  cleanup = [];
  dotnet = null; hover = -1; down = null;
  scene = null; camera = null; canvas = null;
}
