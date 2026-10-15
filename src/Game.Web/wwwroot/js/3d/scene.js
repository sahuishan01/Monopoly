import * as THREE from '../vendor/three.module.min.js';
import { tileMaterial, tokenMaterial, buildingMaterial } from './materials.js';

export function createWorld(board) {
  const side = board.Tiles.length / 4;
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0b0d12);
  const tokens = new Map();
  const tiles = [];

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
    scene.remove(m);
    if (m.geometry) m.geometry.dispose();
    if (m.material) {
      if (m.material.map) m.material.map.dispose();
      m.material.dispose();
    }
  }
  function label(text) {
    const c = document.createElement('canvas');
    c.width = 256; c.height = 256;
    const g = c.getContext('2d');
    g.fillStyle = '#fff'; g.font = 'bold 34px sans-serif'; g.textAlign = 'center'; g.textBaseline = 'middle';
    const lines = [];
    let cur = '';
    for (const w of text.split(' ')) { if ((cur + ' ' + w).trim().length > 11 && cur) { lines.push(cur); cur = w; } else cur = (cur + ' ' + w).trim(); }
    if (cur) lines.push(cur);
    lines.slice(0, 3).forEach((l, i, a) => g.fillText(l, 128, 128 + (i - (a.length - 1) / 2) * 42));
    const m = new THREE.Mesh(new THREE.PlaneGeometry(1.0, 1.0),
      new THREE.MeshBasicMaterial({ map: new THREE.CanvasTexture(c), transparent: true }));
    m.rotation.x = -Math.PI / 2;
    return m;
  }

  scene.add(new THREE.AmbientLight(0xffffff, 0.7));
  const sun = new THREE.DirectionalLight(0xffffff, 0.8);
  sun.position.set(5, 10, 6);
  scene.add(sun);

  const colors = {};
  for (const d of board.Districts || []) colors[d.Id] = d.Color;
  board.Tiles.forEach((t, i) => {
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(1.1, 0.12, 1.1),
      tileMaterial(new THREE.Color(colors[t.District] || '#2a3344')));
    mesh.position.copy(pos(i));
    scene.add(mesh);
    const lab = label(t.Name || '');
    lab.position.copy(pos(i)).setY(0.075);
    scene.add(lab);
    mesh.userData.label = lab;
    tiles.push({ mesh, building: null });
  });

  function ensureToken(p) {
    let t = tokens.get(p.Id);
    if (!t) {
      const mesh = new THREE.Mesh(new THREE.CylinderGeometry(0.22, 0.22, 0.5, 16), tokenMaterial(p.Id));
      scene.add(mesh);
      t = { mesh, target: pos(p.Position).setY(0.35) };
      mesh.position.copy(t.target);
      tokens.set(p.Id, t);
    }
    return t;
  }

  return {
    scene, tiles, tokens, side,
    pickables: () => tiles.map(t => t.mesh),
    applyState(s) {
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
          const b = new THREE.Mesh(new THREE.BoxGeometry(0.6, Math.max(h, 0.2), 0.6), buildingMaterial(prop));
          b.position.copy(pos(i)).setY(h / 2 + 0.06);
          scene.add(b);
          tile.building = b;
        }
      });
    },
    playEvent(e) {
      if (e['$t'] === 'PlayerMoved') {
        const t = tokens.get(e.Player);
        if (t) t.target = pos(e.To).setY(0.35);
      }
    },
    step() { for (const t of tokens.values()) t.mesh.position.lerp(t.target, 0.15); },
    dispose() {
      for (const t of tokens.values()) disposeMesh(t.mesh);
      tokens.clear();
      for (const t of tiles) { disposeMesh(t.building); disposeMesh(t.mesh.userData.label); disposeMesh(t.mesh); }
      tiles.length = 0;
    },
  };
}
