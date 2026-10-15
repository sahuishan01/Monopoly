import * as THREE from '../vendor/three.module.min.js';

// Built-in fallback: the neon-noir values.
export const DEFAULT_THEME = {
  scene: { sky: ['#0b1030', '#02030a'], felt: '#0d3b2e', fog: '#07080f', ambient: 0.35, sun: '#9fb4ff', bloom: 0.9 },
  token: { metal: 0.8, rough: 0.3 },
};

export function mergeTheme(t) {
  const s = (t && t.scene) || {}, k = (t && t.token) || {};
  return {
    scene: { ...DEFAULT_THEME.scene, ...s, sky: Array.isArray(s.sky) && s.sky.length >= 2 ? s.sky : DEFAULT_THEME.scene.sky },
    token: { ...DEFAULT_THEME.token, ...k },
  };
}

export function skyTexture(top, bottom) {
  const c = document.createElement('canvas');
  c.width = 4; c.height = 256;
  const g = c.getContext('2d');
  const gr = g.createLinearGradient(0, 0, 0, 256);
  gr.addColorStop(0, top); gr.addColorStop(1, bottom);
  g.fillStyle = gr; g.fillRect(0, 0, 4, 256);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

// Emissive strength of the district strips: themes with little bloom still get a clearly lit strip.
export function stripGlow(th) { const b = th && th.scene && typeof th.scene.bloom === 'number' ? th.scene.bloom : 0.9; return Math.max(0.6, b) * 1.6; }

// Felt: a soft radial light pool over the theme's felt colour with a fine fibre noise (canvas, no assets).
export function feltTexture(color) {
  const c = document.createElement('canvas');
  c.width = c.height = 256;
  const g = c.getContext('2d');
  const base = new THREE.Color(color), hi = base.clone().lerp(new THREE.Color(0xffffff), 0.18), lo = base.clone().multiplyScalar(0.35);
  const gr = g.createRadialGradient(128, 128, 8, 128, 128, 128);
  gr.addColorStop(0, '#' + hi.getHexString()); gr.addColorStop(0.45, '#' + base.getHexString()); gr.addColorStop(1, '#' + lo.getHexString());
  g.fillStyle = gr; g.fillRect(0, 0, 256, 256);
  const img = g.getImageData(0, 0, 256, 256), d = img.data;
  let seed = 7;
  for (let i = 0; i < d.length; i += 4) {
    seed = (seed * 16807) % 2147483647;
    const n = ((seed / 2147483647) - 0.5) * 14;
    d[i] = Math.max(0, Math.min(255, d[i] + n)); d[i + 1] = Math.max(0, Math.min(255, d[i + 1] + n)); d[i + 2] = Math.max(0, Math.min(255, d[i + 2] + n));
  }
  g.putImageData(img, 0, 0);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

// Centre inlay: a theme-tinted panel with a glowing accent frame, the wordmark and the board name.
export function inlayTexture(boardName, accent, glow, felt) {
  const c = document.createElement('canvas');
  c.width = c.height = 512;
  const g = c.getContext('2d');
  const base = new THREE.Color(felt).multiplyScalar(0.55);
  const gr = g.createRadialGradient(256, 256, 20, 256, 256, 360);
  gr.addColorStop(0, '#' + base.clone().lerp(new THREE.Color(glow), 0.22).getHexString());
  gr.addColorStop(1, '#' + base.clone().multiplyScalar(0.6).getHexString());
  g.fillStyle = gr; g.fillRect(0, 0, 512, 512);
  // diagonal pinstripes
  g.strokeStyle = 'rgba(255,255,255,0.035)'; g.lineWidth = 6;
  for (let x = -512; x < 512; x += 28) { g.beginPath(); g.moveTo(x, 512); g.lineTo(x + 512, 0); g.stroke(); }
  const frame = (inset, w, col, blur) => { g.save(); g.shadowColor = col; g.shadowBlur = blur; g.strokeStyle = col; g.lineWidth = w; g.strokeRect(inset, inset, 512 - inset * 2, 512 - inset * 2); g.restore(); };
  frame(18, 6, accent, 18); frame(34, 2, glow, 6);
  g.textAlign = 'center'; g.textBaseline = 'middle';
  g.save(); g.translate(256, 256); g.rotate(-Math.PI / 4);
  g.shadowColor = glow; g.shadowBlur = 24; g.fillStyle = accent;
  g.font = '900 64px system-ui, "Segoe UI", sans-serif';
  g.fillText('BOARD', 0, -34); g.fillText('EMPIRE', 0, 34);
  g.shadowBlur = 0; g.fillStyle = 'rgba(255,255,255,0.75)';
  g.font = '600 26px system-ui, "Segoe UI", sans-serif';
  if (boardName) g.fillText(String(boardName).toUpperCase(), 0, 100);
  g.restore();
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

// Procedural PMREM environment from the sky gradient.
export function buildEnvironment(renderer, top, bottom) {
  const tex = skyTexture(top, bottom);
  const env = new THREE.Scene();
  env.add(new THREE.Mesh(new THREE.SphereGeometry(50, 32, 16), new THREE.MeshBasicMaterial({ map: tex, side: THREE.BackSide })));
  const pm = new THREE.PMREMGenerator(renderer);
  const rt = pm.fromScene(env, 0.04);
  pm.dispose();
  env.children[0].geometry.dispose(); env.children[0].material.dispose(); tex.dispose();
  return rt;
}

// Material factory that remembers what it made so a theme change recolours in place.
export function createLook(theme) {
  let th = mergeTheme(theme);
  const tokens = new Set(), strips = new Set(), all = new Set(), felt = new THREE.MeshStandardMaterial({ color: th.scene.felt, roughness: 0.95 });
  all.add(felt);
  const track = (set, m) => { set.add(m); all.add(m); return m; };
  const look = {
    get theme() { return th; },
    felt,
    tile(color) { return track(new Set(), new THREE.MeshPhysicalMaterial({ color, roughness: 0.55, metalness: 0.1, clearcoat: 0.2 })); },
    strip(color) {
      return track(strips, new THREE.MeshStandardMaterial({ color, emissive: new THREE.Color(color), emissiveIntensity: stripGlow(th), roughness: 0.4 }));
    },
    token(id) {
      const col = new THREE.Color().setHSL((id * 0.17) % 1, 0.75, 0.55);
      return track(tokens, new THREE.MeshPhysicalMaterial({
        color: col, emissive: col.clone(), emissiveIntensity: 0.18,
        metalness: th.token.metal, roughness: th.token.rough, clearcoat: 1, clearcoatRoughness: 0.08 }));
    },
    building(prop) {
      return track(new Set(), new THREE.MeshStandardMaterial({
        color: prop.Owner >= 0 ? new THREE.Color().setHSL((prop.Owner * 0.17) % 1, 0.6, 0.5) : 0x555555,
        roughness: 0.6, transparent: prop.Mortgaged, opacity: prop.Mortgaged ? 0.4 : 1 }));
    },
    setTheme(t) {
      th = mergeTheme(t);
      felt.color.set(th.scene.felt);
      for (const m of tokens) { m.metalness = th.token.metal; m.roughness = th.token.rough; }
      for (const m of strips) m.emissiveIntensity = stripGlow(th);
    },
    release(m) { all.delete(m); tokens.delete(m); strips.delete(m); },
    markDirty() { for (const m of all) m.needsUpdate = true; },
  };
  return look;
}
