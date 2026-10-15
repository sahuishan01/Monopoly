import * as THREE from '../vendor/three.module.min.js';
import { skyTexture, buildEnvironment } from './materials.js';
import { Motion, Tween, ease, DICE_FACE_VALUES } from './motion.js';

export function createWorld(board, look, renderer, opts = {}) {
  const side = board.Tiles.length / 4;
  const scene = new THREE.Scene();
  scene.fog = new THREE.Fog(0x000000, 28, 75);
  const tokens = new Map();
  const tiles = [];
  const motion = new Motion({ posOf: t => pos(t), n: board.Tiles.length, reduce: !!opts.reduceMotion });
  let prevLevels = null;

  const diceGroup = new THREE.Group();
  diceGroup.visible = false; diceGroup.position.set(0, 0, 0);
  scene.add(diceGroup);
  const pipTex = DICE_FACE_VALUES.map(v => {
    const c = document.createElement('canvas'); c.width = c.height = 64;
    const g = c.getContext('2d');
    g.fillStyle = '#f5f1e6'; g.fillRect(0, 0, 64, 64); g.fillStyle = '#222';
    const P = { 1: [[1, 1]], 2: [[0, 0], [2, 2]], 3: [[0, 0], [1, 1], [2, 2]], 4: [[0, 0], [2, 0], [0, 2], [2, 2]],
      5: [[0, 0], [2, 0], [1, 1], [0, 2], [2, 2]], 6: [[0, 0], [2, 0], [0, 1], [2, 1], [0, 2], [2, 2]] }[v];
    for (const [x, y] of P) { g.beginPath(); g.arc(14 + x * 18, 14 + y * 18, 5, 0, 7); g.fill(); }
    return new THREE.CanvasTexture(c);
  });
  const diceMats = pipTex.map(map => new THREE.MeshStandardMaterial({ map, roughness: 0.4 }));
  const diceGeo = new THREE.BoxGeometry(0.6, 0.6, 0.6);
  const cubes = [0, 1].map(() => { const m = new THREE.Mesh(diceGeo, diceMats); m.castShadow = true; diceGroup.add(m); return m; });

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
    mesh.userData.base = col.clone();
    const lab = label(t.Name || '');
    lab.position.copy(pos(i)).setY(0.075);
    scene.add(lab);
    mesh.userData.label = lab;
    tiles.push({ mesh, building: null, aura: null, sig: '', pendingSpring: false });
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
        t.mesh.scale.set(1, 1, 1);
        t.target = pos(p.Position).setY(0.35);
        if (!t.busy) t.mesh.position.copy(t.target);
        t.tile = p.Position;
      }
      s.Properties.forEach((prop, i) => {
        if (!prop || !tiles[i]) return;
        const tile = tiles[i];
        const sig = prop.Owner + ':' + prop.Level + ':' + !!prop.Mortgaged;
        if (sig === tile.sig && (tile.building || tile.aura || (prop.Level <= 0 && prop.Owner < 0))) return;
        tile.sig = sig;
        const springIt = tile.pendingSpring; tile.pendingSpring = false;
        if (tile.building) { motion.cancel(tile.building); disposeMesh(tile.building); tile.building = null; }
        if (tile.aura) { motion.cancel(tile.aura); disposeMesh(tile.aura); tile.aura = null; }
        // mortgaged tiles desaturate and dim
        const mort = !!prop.Mortgaged, base = tile.mesh.userData.base, strip = tile.mesh.userData.strip;
        const gray = base.r * 0.3 + base.g * 0.59 + base.b * 0.11;
        const dim = mort ? new THREE.Color(gray, gray, gray).multiplyScalar(0.5) : base.clone().multiplyScalar(0.6);
        tile.mesh.material.color.copy(dim);
        strip.material.color.copy(mort ? new THREE.Color(gray, gray, gray) : base);
        strip.material.emissive.copy(strip.material.color);
        strip.material.emissiveIntensity = look.theme.scene.bloom * (mort ? 0.2 : 1);
        if (prop.Owner >= 0) {
          const hue = (prop.Owner * 0.17) % 1;
          const ring = new THREE.Mesh(new THREE.RingGeometry(0.5, 0.78, 32),
            new THREE.MeshBasicMaterial({ color: new THREE.Color().setHSL(hue, 0.8, mort ? 0.3 : 0.55), transparent: true, opacity: mort ? 0.35 : 0.85,
              blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide }));
          ring.rotation.x = -Math.PI / 2;
          ring.position.copy(pos(i)).setY(-0.055);
          scene.add(ring); tile.aura = ring;
          if (springIt) motion.spring(ring, 0, 1, 0.5);
        }
        if (prop.Level > 0 || prop.Owner >= 0) {
          const lvl = prop.Level, h = 0.12 + Math.min(lvl, 4) * 0.25;
          let b;
          if (lvl >= 5) {
            b = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.3, 1.5, 8), look.building(prop));
            b.position.copy(pos(i)).setY(0.75 + 0.06);
          } else {
            b = new THREE.Mesh(new THREE.BoxGeometry(0.6, Math.max(h, 0.2), 0.6), look.building(prop));
            b.position.copy(pos(i)).setY(h / 2 + 0.06);
          }
          b.castShadow = b.receiveShadow = true;
          scene.add(b); tile.building = b;
          if (springIt) { b.scale.set(0, 0, 0); motion.spring(b, 0, 1, 0.55); }
        }
      });
    },
    playEvent(e, burst) {
      const type = e['$t'];
      const fast = !!burst;
      const tok = tokens.get(e.Player);
      switch (type) {
        case 'PlayerMoved':
          if (tok) {
            const to = pos(e.To).setY(0.35);
            tok.target = to; tok.tile = e.To;
            motion.cancel(tok);
            if (fast || motion.reduce) { tok.mesh.position.copy(to); tok.mesh.scale.set(1, 1, 1); tok.busy = false; }
            else motion.hop(tok, e.From, e.To, 0.16);
          }
          break;
        case 'PlayerSentToJail':
          if (tok) {
            const to = pos(e.JailTile).setY(0.35);
            tok.target = to;
            motion.cancel(tok);
            if (fast || motion.reduce) { tok.mesh.position.copy(to); tok.busy = false; }
            else motion.hop(tok, tok.tile ?? 0, e.JailTile, 0.16);
            tok.tile = e.JailTile;
          }
          break;
        case 'DiceRolled':
          if (!fast && !motion.reduce) motion.dice(diceGroup, cubes, e.D1, e.D2);
          break;
        case 'BuildingConstructed':
        case 'PropertyPurchased': {
          const tile = tiles[e.Tile];
          if (!tile || fast || motion.reduce) break;
          if (tile.building) { tile.building.scale.set(0, 0, 0); motion.spring(tile.building, 0, 1, 0.55); }
          else tile.pendingSpring = true;
          if (tile.aura) { motion.spring(tile.aura, 0, 1, 0.5); }
          break;
        }
        case 'PlayerBankrupt':
          if (tok && !fast && !motion.reduce) {
            motion.add(new Tween(0.5, ease.inOutQuad, u => { const s = 1 - u; tok.mesh.scale.set(s, s, s); },
              () => { tok.mesh.visible = false; tok.mesh.scale.set(1, 1, 1); }), tok.mesh);
          } else if (tok) tok.mesh.visible = false;
          break;
        case 'GameEnded':
          if (!fast && !motion.reduce) for (const w of e.Winners || []) { const t = tokens.get(w); if (t) motion.spring(t.mesh, 1.6, 1, 0.8); }
          break;
        default: break;
      }
    },
    tokenPos(id) { const t = tokens.get(id); return t ? t.mesh.position : null; },
    setReduceMotion(v) { motion.setReduce(v); },
    step(dt = 0.016) {
      motion.update(dt);
      for (const t of tokens.values()) if (!t.busy) t.mesh.position.lerp(t.target, 0.15);
    },
    dispose() {
      motion.clear();
      scene.remove(diceGroup); diceGeo.dispose(); diceMats.forEach(m => m.dispose()); pipTex.forEach(t => t.dispose());
      for (const t of tiles) { if (t.aura) { disposeMesh(t.aura); t.aura = null; } }
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
