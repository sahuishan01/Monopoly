import * as THREE from '../vendor/three.module.min.js';
import { skyTexture, buildEnvironment, feltTexture, inlayTexture, stripGlow } from './materials.js';
import { Motion, Tween, ease, DICE_FACE_VALUES } from './motion.js';

const TOKEN_Y = 0.06;   // tokens stand on the tile top (the pawn geometry has its origin at its base)
const TILE_TINT = 0.75; // tile body = district colour x this (the strip carries the full colour)
// Where each seat stands inside a shared tile: front half, so the building at the back stays visible.
const SEAT = [[-0.22, 0.12], [0.22, 0.12], [-0.22, 0.34], [0.22, 0.34], [0, 0.22], [0, 0.4], [-0.32, 0.4], [0.32, 0.4]];

// A lathe-turned pawn: base, waist, collar and head. Origin at the base so it stands on any surface.
function pawnGeometry(THREE) {
  const p = [[0, 0], [0.25, 0], [0.26, 0.03], [0.24, 0.06], [0.17, 0.09], [0.12, 0.13], [0.09, 0.28], [0.15, 0.32], [0.15, 0.34], [0.07, 0.37]];
  for (let a = -60; a <= 90; a += 15) { const r = a * Math.PI / 180; p.push([Math.max(0, 0.13 * Math.cos(r)), 0.48 + 0.13 * Math.sin(r)]); }
  return new THREE.LatheGeometry(p.map(([x, y]) => new THREE.Vector2(x, y)), 20);
}

export function createWorld(board, look, renderer, opts = {}) {
  const side = board.Tiles.length / 4;
  let clock = 0;
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
  const aniso = Math.min(4, renderer.capabilities && renderer.capabilities.getMaxAnisotropy ? renderer.capabilities.getMaxAnisotropy() : 1);
  // Tile name, word-wrapped by measured width (never mid-word); the font shrinks until the longest word fits.
  function label(text) {
    const c = document.createElement('canvas');
    c.width = 256; c.height = 192;
    const g = c.getContext('2d');
    g.textAlign = 'center'; g.textBaseline = 'middle'; g.lineJoin = 'round';
    const words = String(text).split(/\s+/).filter(Boolean), maxW = 236;
    let size = 46, lines = [];
    for (; size >= 24; size -= 2) {
      g.font = `800 ${size}px system-ui, "Segoe UI", sans-serif`;
      lines = []; let cur = '';
      for (const w of words) { const t = cur ? cur + ' ' + w : w; if (cur && g.measureText(t).width > maxW) { lines.push(cur); cur = w; } else cur = t; }
      if (cur) lines.push(cur);
      if (lines.length <= 3 && lines.every(l => g.measureText(l).width <= maxW)) break;
    }
    const lh = size * 1.12;
    lines.slice(0, 3).forEach((l, i, a) => {
      const y = 96 + (i - (a.length - 1) / 2) * lh;
      g.strokeStyle = 'rgba(0,0,0,0.7)'; g.lineWidth = Math.max(4, size * 0.16); g.strokeText(l, 128, y);
      g.fillStyle = '#fff'; g.fillText(l, 128, y);
    });
    const tex = new THREE.CanvasTexture(c);
    tex.colorSpace = THREE.SRGBColorSpace; tex.anisotropy = aniso;
    const m = new THREE.Mesh(new THREE.PlaneGeometry(0.98, 0.735),
      new THREE.MeshBasicMaterial({ map: tex, transparent: true, depthWrite: false, toneMapped: false }));
    m.rotation.x = -Math.PI / 2;
    return m;
  }

  // Key (sun, the only shadow caster), sky/ground hemisphere fill, a soft front fill and a coloured back rim.
  const ambient = new THREE.AmbientLight(0xffffff, 0.3);
  scene.add(ambient);
  const hemi = new THREE.HemisphereLight(0xffffff, 0x202020, 0.9);
  scene.add(hemi);
  const fill = new THREE.DirectionalLight(0xffffff, 0.55);
  fill.position.set(-side * 0.8, side * 0.7, side * 1.1);
  scene.add(fill);
  const rim = new THREE.DirectionalLight(0x88aaff, 1.1);
  rim.position.set(side * 0.3, side * 0.5, -side * 1.2);
  scene.add(rim);
  const sun = new THREE.DirectionalLight(0xffffff, 2.4);
  sun.position.set(side * 0.5, side * 1.2, side * 0.6);
  const ext = side * 0.9;
  Object.assign(sun.shadow.camera, { left: -ext, right: ext, top: ext, bottom: -ext, near: 1, far: side * 4 });
  sun.shadow.camera.updateProjectionMatrix();
  sun.shadow.mapSize.set(2048, 2048);
  sun.shadow.bias = -0.0004; sun.shadow.radius = 4;
  scene.add(sun);

  const feltSize = side * 2.4 + 6;   // wide enough that a far-fitted portrait camera never sees past the table
  const felt = new THREE.Mesh(new THREE.PlaneGeometry(feltSize, feltSize), look.felt);
  felt.rotation.x = -Math.PI / 2; felt.position.y = -0.2; felt.receiveShadow = true;
  scene.add(felt);

  // The board itself: a rounded slab the tiles sit on, and a glowing centre inlay with the board's name.
  function roundedRect(w, r) {
    const h = w / 2, sh = new THREE.Shape();
    sh.moveTo(-h + r, -h); sh.lineTo(h - r, -h); sh.quadraticCurveTo(h, -h, h, -h + r);
    sh.lineTo(h, h - r); sh.quadraticCurveTo(h, h, h - r, h); sh.lineTo(-h + r, h);
    sh.quadraticCurveTo(-h, h, -h, h - r); sh.lineTo(-h, -h + r); sh.quadraticCurveTo(-h, -h, -h + r, -h);
    return sh;
  }
  const slabW = side * 1.2 + 1.5;
  const slabGeo = new THREE.ExtrudeGeometry(roundedRect(slabW, 0.4), { depth: 0.14, bevelEnabled: true, bevelThickness: 0.03, bevelSize: 0.05, bevelSegments: 2, curveSegments: 6 });
  slabGeo.rotateX(-Math.PI / 2); slabGeo.translate(0, -0.2, 0);
  const slabMat = new THREE.MeshStandardMaterial({ color: 0x222222, roughness: 0.55, metalness: 0.2 });
  const slab = new THREE.Mesh(slabGeo, slabMat);
  slab.receiveShadow = true;
  scene.add(slab);
  const inlayW = (side - 1) * 1.2 - 0.25;
  const inlayMat = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.7, emissive: 0xffffff, emissiveIntensity: 0.4 });
  const inlay = new THREE.Mesh(new THREE.PlaneGeometry(inlayW, inlayW), inlayMat);
  inlay.rotation.x = -Math.PI / 2; inlay.position.y = -0.02; inlay.receiveShadow = true;
  scene.add(inlay);

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
    ambient.intensity = t.ambient * 0.8;
    sun.color.set(t.sun);
    const accent = (theme && theme.css && theme.css['--accent']) || '#88aaff';
    const glow = (theme && theme.css && theme.css['--glow']) || accent;
    hemi.color.set(t.sky[0]).lerp(new THREE.Color(0xffffff), 0.6);
    hemi.groundColor.set(t.felt);
    rim.color.set(glow);
    renderer.toneMappingExposure = typeof t.exposure === 'number' ? t.exposure : 1.3;
    if (look.felt.map) look.felt.map.dispose();
    look.felt.map = feltTexture(t.felt); look.felt.color.set(0xffffff); look.felt.needsUpdate = true;
    slabMat.color.set(t.felt).lerp(new THREE.Color(t.sky[1]), 0.55).multiplyScalar(0.8);
    if (inlayMat.map) inlayMat.map.dispose();
    inlayMat.map = inlayTexture(board.Name || '', accent, glow, t.felt);
    inlayMat.emissiveMap = inlayMat.map; inlayMat.needsUpdate = true;
    for (const tl of tiles) if (tl.mort) tl.mesh.userData.strip.material.emissiveIntensity = stripGlow(look.theme) * 0.2;
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
  const stripGeo = new THREE.BoxGeometry(0.92, 0.035, 0.17);

  const colors = {};
  for (const d of board.Districts || []) colors[d.Id] = d.Color;
  board.Tiles.forEach((t, i) => {
    const col = new THREE.Color(colors[t.District] || '#3a4660');
    const mesh = new THREE.Mesh(tileGeo, look.tile(col.clone().multiplyScalar(TILE_TINT)));
    mesh.position.copy(pos(i));
    mesh.castShadow = mesh.receiveShadow = true;
    scene.add(mesh);
    const strip = new THREE.Mesh(stripGeo, look.strip(col));
    strip.position.copy(pos(i)).add(new THREE.Vector3(0, 0.07, -0.4));
    scene.add(strip);
    mesh.userData.strip = strip;
    mesh.userData.base = col.clone();
    const lab = label(t.Name || '');
    lab.position.copy(pos(i)).add(new THREE.Vector3(0, 0.072, 0.14));
    scene.add(lab);
    mesh.userData.label = lab;
    tiles.push({ mesh, building: null, aura: null, sig: '', pendingSpring: false });
  });

  const pawnGeo = pawnGeometry(THREE);
  // Token resting point on a tile: its seat offset, so tokens sharing a tile stand side by side.
  function seatPos(id, tile) {
    const [dx, dz] = SEAT[((id % SEAT.length) + SEAT.length) % SEAT.length];
    return pos(tile).add(new THREE.Vector3(dx, TOKEN_Y, dz));
  }
  function ensureToken(p) {
    let t = tokens.get(p.Id);
    if (!t) {
      const mesh = new THREE.Mesh(pawnGeo, look.token(p.Id));
      mesh.castShadow = true;
      scene.add(mesh);
      t = { mesh, id: p.Id, baseY: TOKEN_Y, target: seatPos(p.Id, p.Position) };
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
        t.target = seatPos(p.Id, p.Position);
        if (!t.busy) t.mesh.position.copy(t.target);
        if (!t.busy && !motion.has(t.mesh)) t.mesh.scale.set(1, 1, 1);
        if (t.tile !== p.Position) { t.prev = t.tile; t.tile = p.Position; }
      }
      s.Properties.forEach((prop, i) => {
        if (!prop || !tiles[i]) return;
        const tile = tiles[i];
        const sig = prop.Owner + ':' + prop.Level + ':' + !!prop.Mortgaged;
        if (sig === tile.sig && (tile.building || tile.aura || (prop.Level <= 0 && prop.Owner < 0))) { tile.pendingSpring = false; return; }
        tile.sig = sig;
        const springIt = tile.pendingSpring; tile.pendingSpring = false;
        if (tile.building) { motion.cancel(tile.building); disposeMesh(tile.building); tile.building = null; }
        if (tile.aura) { motion.cancel(tile.aura); disposeMesh(tile.aura); tile.aura = null; }
        // mortgaged tiles desaturate and dim
        const mort = !!prop.Mortgaged; tile.mort = mort;
        const base = tile.mesh.userData.base, strip = tile.mesh.userData.strip;
        const gray = base.r * 0.3 + base.g * 0.59 + base.b * 0.11;
        const dim = mort ? new THREE.Color(gray, gray, gray).multiplyScalar(0.5) : base.clone().multiplyScalar(TILE_TINT);
        tile.mesh.material.color.copy(dim);
        strip.material.color.copy(mort ? new THREE.Color(gray, gray, gray) : base);
        strip.material.emissive.copy(strip.material.color);
        strip.material.emissiveIntensity = stripGlow(look.theme) * (mort ? 0.2 : 1);
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
          // Buildings stand at the back of the tile (by the strip), so the name in front stays readable.
          const lvl = prop.Level, h = 0.1 + Math.min(lvl, 4) * 0.17;
          const back = pos(i).add(new THREE.Vector3(0, 0, -0.25));
          let b;
          if (lvl >= 5) {
            b = new THREE.Mesh(new THREE.CylinderGeometry(0.09, 0.22, 1.0, 8), look.building(prop));
            b.position.copy(back).setY(0.5 + 0.06);
          } else {
            b = new THREE.Mesh(new THREE.BoxGeometry(0.44, Math.max(h, 0.14), 0.28), look.building(prop));
            b.position.copy(back).setY(Math.max(h, 0.14) / 2 + 0.06);
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
            const to = seatPos(e.Player, e.To);
            tok.target = to; tok.prev = e.From; tok.tile = e.To;
            motion.cancel(tok);
            if (fast || motion.reduce) { tok.mesh.position.copy(to); tok.mesh.scale.set(1, 1, 1); tok.busy = false; }
            else motion.hop(tok, e.From, e.To, 0.16);
          }
          break;
        case 'PlayerSentToJail':
          if (tok) {
            const to = seatPos(e.Player, e.JailTile);
            tok.target = to;
            motion.cancel(tok);
            const jf = tok.tile !== e.JailTile ? tok.tile : (tok.prev ?? e.JailTile);
            if (fast || motion.reduce) { tok.mesh.position.copy(to); tok.busy = false; }
            else motion.hop(tok, jf ?? 0, e.JailTile, 0.16);
            tok.prev = jf; tok.tile = e.JailTile;
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
      clock += Math.min(Math.max(dt || 0, 0), 0.1);
      // Resting tokens ease to their seat and bob gently (not under reduced motion).
      for (const t of tokens.values()) {
        if (t.busy) continue;
        const bob = motion.reduce ? 0 : 0.035 * (1 + Math.sin(clock * 2.2 + t.id * 1.7));
        const m = t.mesh.position;
        m.x += (t.target.x - m.x) * 0.15; m.z += (t.target.z - m.z) * 0.15; m.y += (t.target.y + bob - m.y) * 0.15;
      }
    },
    dispose() {
      motion.clear();
      scene.remove(diceGroup); diceGeo.dispose(); diceMats.forEach(m => m.dispose()); pipTex.forEach(t => t.dispose());
      for (const t of tiles) { if (t.aura) { disposeMesh(t.aura); t.aura = null; } }
      for (const t of tokens.values()) disposeMesh(t.mesh);
      tokens.clear();
      for (const t of tiles) { disposeMesh(t.building); disposeMesh(t.mesh.userData.label); disposeMesh(t.mesh.userData.strip); t.mesh.geometry = null; disposeMesh(t.mesh); }
      tiles.length = 0;
      tileGeo.dispose(); stripGeo.dispose(); pawnGeo.dispose(); felt.geometry.dispose();
      if (look.felt.map) { look.felt.map.dispose(); look.felt.map = null; }
      look.felt.dispose();
      scene.remove(slab); slabGeo.dispose(); slabMat.dispose();
      scene.remove(inlay); inlay.geometry.dispose(); if (inlayMat.map) inlayMat.map.dispose(); inlayMat.dispose();
      if (envRT) envRT.dispose();
      if (scene.background && scene.background.dispose) scene.background.dispose();
      scene.environment = null;
    },
  };
}
