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
      return track(strips, new THREE.MeshStandardMaterial({ color, emissive: new THREE.Color(color), emissiveIntensity: th.scene.bloom, roughness: 0.4 }));
    },
    token(id) {
      return track(tokens, new THREE.MeshPhysicalMaterial({
        color: new THREE.Color().setHSL((id * 0.17) % 1, 0.7, 0.55),
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
      for (const m of strips) m.emissiveIntensity = th.scene.bloom;
    },
    markDirty() { for (const m of all) m.needsUpdate = true; },
  };
  return look;
}
