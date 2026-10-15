import * as THREE from '../vendor/three.module.min.js';
import { skyTexture, buildEnvironment } from './materials.js';

export function createWorld(board, look, renderer) {
  const side = board.Tiles.length / 4;
  const scene = new THREE.Scene();
  scene.fog = new THREE.Fog(0x000000, 28, 75);
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
      look.release(m.material);
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

  const ambient = new THREE.AmbientLight(0xffffff, 0.5);
  scene.add(ambient);
  const sun = new THREE.DirectionalLight(0xffffff, 1.6);
  sun.position.set(side * 0.5, side * 1.2, side * 0.6);
  const ext = side * 0.9;
  Object.assign(sun.shadow.camera, { left: -ext, right: ext, top: ext, bottom: -ext, near: 1, far: side * 4 });
  sun.shadow.camera.updateProjectionMatrix();
  sun.shadow.mapSize.set(2048, 2048);
  sun.shadow.bias = -0.0004; sun.shadow.radius = 4;
  scene.add(sun);

  const feltSize = side * 1.2 + 5;
  const felt = new THREE.Mesh(new THREE.PlaneGeometry(feltSize, feltSize), look.felt);
  felt.rotation.x = -Math.PI / 2; felt.position.y = -0.07; felt.receiveShadow = true;
  scene.add(felt);

  let envRT = null;
  function setTheme(theme) {
    look.setTheme(theme);
    const t = look.theme.scene;
    if (scene.background && scene.background.dispose) scene.background.dispose();
    scene.background = skyTexture(t.sky[0], t.sky[1]);
    if (envRT) envRT.dispose();
    envRT = buildEnvironment(renderer, t.sky[0], t.sky[1]);
    scene.environment = envRT.texture;
    scene.fog.color.set(t.fog);
    ambient.intensity = t.ambient * 1.4;
    sun.color.set(t.sun);
  }
  function setQuality(q) {
    const on = q !== 'low';
    renderer.shadowMap.enabled = on;
    sun.castShadow = on;
    look.markDirty();
  }

  function bevelTile() {
    const w = 1.04, r = 0.08, h = w / 2, sh = new THREE.Shape();
    sh.moveTo(-h + r, -h); sh.lineTo(h - r, -h); sh.quadraticCurveTo(h, -h, h, -h + r);
    sh.lineTo(h, h - r); sh.quadraticCurveTo(h, h, h - r, h); sh.lineTo(-h + r, h);
    sh.quadraticCurveTo(-h, h, -h, h - r); sh.lineTo(-h, -h + r); sh.quadraticCurveTo(-h, -h, -h + r, -h);
    const g = new THREE.ExtrudeGeometry(sh, { depth: 0.08, bevelEnabled: true, bevelThickness: 0.02, bevelSize: 0.03, bevelSegments: 2, curveSegments: 4 });
    g.rotateX(-Math.PI / 2); g.translate(0, -0.04, 0);
    return g;
  }
  const tileGeo = bevelTile();
  const stripGeo = new THREE.BoxGeometry(0.9, 0.02, 0.1);

  const colors = {};
  for (const d of board.Districts || []) colors[d.Id] = d.Color;
  board.Tiles.forEach((t, i) => {
    const col = new THREE.Color(colors[t.District] || '#2a3344');
    const mesh = new THREE.Mesh(tileGeo, look.tile(col.clone().multiplyScalar(0.6)));
    mesh.position.copy(pos(i));
    mesh.castShadow = mesh.receiveShadow = true;
    scene.add(mesh);
    const strip = new THREE.Mesh(stripGeo, look.strip(col));
    strip.position.copy(pos(i)).add(new THREE.Vector3(0, 0.07, -0.42));
    scene.add(strip);
    mesh.userData.strip = strip;
    const lab = label(t.Name || '');
    lab.position.copy(pos(i)).setY(0.075);
    scene.add(lab);
    mesh.userData.label = lab;
    tiles.push({ mesh, building: null });
  });

  function ensureToken(p) {
    let t = tokens.get(p.Id);
    if (!t) {
      const mesh = new THREE.Mesh(new THREE.CylinderGeometry(0.22, 0.22, 0.5, 16), look.token(p.Id));
      mesh.castShadow = true;
      scene.add(mesh);
      t = { mesh, target: pos(p.Position).setY(0.35) };
      mesh.position.copy(t.target);
      tokens.set(p.Id, t);
    }
    return t;
  }

  return {
    scene, tiles, tokens, side, setTheme, setQuality,
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
          const b = new THREE.Mesh(new THREE.BoxGeometry(0.6, Math.max(h, 0.2), 0.6), look.building(prop));
          b.castShadow = b.receiveShadow = true;
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
      for (const t of tiles) { disposeMesh(t.building); disposeMesh(t.mesh.userData.label); disposeMesh(t.mesh.userData.strip); t.mesh.geometry = null; disposeMesh(t.mesh); }
      tiles.length = 0;
      tileGeo.dispose(); stripGeo.dispose(); felt.geometry.dispose(); look.felt.dispose();
      if (envRT) envRT.dispose();
      if (scene.background && scene.background.dispose) scene.background.dispose();
      scene.environment = null;
    },
  };
}
