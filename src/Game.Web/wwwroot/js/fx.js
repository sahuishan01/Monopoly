// Particles, optional bloom/vignette post-processing and quality probing.
// THREE is injected so the pure parts (pool, probe) are testable in node.

export const CAP_HIGH = 400;
export const CAP_LOW = 80;
export const SLOW_FRAME_MS = 28;

export function capFor(q) { return q === 'low' ? CAP_LOW : CAP_HIGH; }

// Ring-buffer pool: spawning past the cap recycles the oldest slot.
export class ParticlePool {
  constructor(max, cap) {
    this.max = max;
    this.cap = Math.min(cap, max);
    this.pos = new Float32Array(max * 3);
    this.vel = new Float32Array(max * 3);
    this.col = new Float32Array(max * 3);
    this.life = new Float32Array(max);
    this.span = new Float32Array(max);
    this.grav = new Float32Array(max);
    this.next = 0;
    this.pos.fill(0); for (let i = 0; i < max; i++) this.pos[i * 3 + 1] = -1000;
  }
  setCap(c) {
    this.cap = Math.min(c, this.max);
    for (let i = this.cap; i < this.max; i++) this.kill(i);
    if (this.next >= this.cap) this.next = 0;
  }
  kill(i) { this.life[i] = 0; this.pos[i * 3 + 1] = -1000; this.col[i * 3] = this.col[i * 3 + 1] = this.col[i * 3 + 2] = 0; }
  alive() { let n = 0; for (let i = 0; i < this.max; i++) if (this.life[i] > 0) n++; return n; }
  spawn(x, y, z, vx, vy, vz, r, g, b, life, grav = 0) {
    if (this.cap <= 0) return -1;
    const i = this.next; this.next = (this.next + 1) % this.cap;
    const k = i * 3;
    this.pos[k] = x; this.pos[k + 1] = y; this.pos[k + 2] = z;
    this.vel[k] = vx; this.vel[k + 1] = vy; this.vel[k + 2] = vz;
    this.col[k] = r; this.col[k + 1] = g; this.col[k + 2] = b;
    this.life[i] = life; this.span[i] = life; this.grav[i] = grav;
    return i;
  }
  step(dt) {
    for (let i = 0; i < this.cap; i++) {
      if (this.life[i] <= 0) continue;
      this.life[i] -= dt;
      if (this.life[i] <= 0) { this.kill(i); continue; }
      const k = i * 3;
      this.vel[k + 1] -= this.grav[i] * dt;
      this.pos[k] += this.vel[k] * dt; this.pos[k + 1] += this.vel[k + 1] * dt; this.pos[k + 2] += this.vel[k + 2] * dt;
      if (this.pos[k + 1] < 0.02 && this.grav[i] > 0) { this.pos[k + 1] = 0.02; this.vel[k + 1] *= -0.3; }
    }
  }
}

// Median frame interval over `frames` samples; > 28 ms means the device struggles.
// Intervals > MAX_GAP_MS (tab resume, long hitch) are discarded and a hidden
// document restarts the sample set, so neither can cause a false "low".
export const MAX_GAP_MS = 250;
export const MAX_OVER_GAP = 5;
export function probeQuality(renderFrame, frames = 20, now = () => performance.now(),
  hidden = () => typeof document !== 'undefined' && document.hidden) {
  return new Promise(resolve => {
    const samples = [];
    let prev = null, over = 0;
    const tick = () => {
      let t;
      try { renderFrame(); t = now(); } catch { resolve('low'); return; }
      if (hidden()) { samples.length = 0; prev = null; over = 0; schedule(tick); return; }
      if (prev !== null) {
        const d = t - prev;
        if (d <= MAX_GAP_MS) { samples.push(d); over = 0; }
        else if (++over >= MAX_OVER_GAP) { resolve('low'); return; } // consistently crawling while visible
      }
      prev = t;
      if (samples.length >= frames) {
        samples.sort((x, y) => x - y);
        resolve(samples[samples.length >> 1] > SLOW_FRAME_MS ? 'low' : 'high');
      } else schedule(tick);
    };
    schedule(tick);
  });
}
function schedule(f) { if (typeof requestAnimationFrame === 'function') requestAnimationFrame(f); else setTimeout(f, 16); }

const KIND = {
  cash: { c: [1, 0.82, 0.25], n: 18, life: 1.1 },
  build: { c: [0.85, 0.8, 0.7], n: 24, life: 0.8 },
  win: { c: null, n: 90, life: 2.2 },
  trail: { c: [1, 0.85, 0.3], n: 14, life: 0.9 },
};

export function createFx(THREE, scene, renderer, camera, theme, quality) {
  let q = quality === 'low' ? 'low' : 'high';
  let th = theme || null;
  let disposed = false, gen = 0, failed = false;
  let composer = null, bloom = null, vignette = null;
  let size = [0, 0];

  const pool = new ParticlePool(CAP_HIGH, capFor(q));
  const geo = new THREE.BufferGeometry();
  const posAttr = new THREE.BufferAttribute(pool.pos, 3); posAttr.setUsage(THREE.DynamicDrawUsage);
  const colAttr = new THREE.BufferAttribute(pool.col, 3); colAttr.setUsage(THREE.DynamicDrawUsage);
  geo.setAttribute('position', posAttr); geo.setAttribute('color', colAttr);
  const mat = new THREE.PointsMaterial({ size: 0.16, vertexColors: true, transparent: true, depthWrite: false,
    blending: THREE.AdditiveBlending, sizeAttenuation: true });
  const points = new THREE.Points(geo, mat);
  points.frustumCulled = false;
  scene.add(points);

  const rnd = (a, b) => a + Math.random() * (b - a);
  function burst(p, kind = 'cash') {
    if (disposed || !p) return;
    const k = KIND[kind] || KIND.cash;
    for (let i = 0; i < k.n; i++) {
      let c = k.c;
      if (!c) c = [Math.random(), Math.random(), Math.random()].map(v => 0.4 + v * 0.6);
      if (kind === 'build') {
        const a = (i / k.n) * Math.PI * 2;
        pool.spawn(p.x, p.y + 0.1, p.z, Math.cos(a) * 1.2, rnd(0.2, 0.7), Math.sin(a) * 1.2, c[0], c[1], c[2], k.life, 2);
      } else if (kind === 'win') {
        pool.spawn(p.x + rnd(-3, 3), p.y + 5 + rnd(0, 2), p.z + rnd(-3, 3), rnd(-0.5, 0.5), rnd(-1, 0), rnd(-0.5, 0.5), c[0], c[1], c[2], k.life, 1.5);
      } else {
        pool.spawn(p.x, p.y + 0.4, p.z, rnd(-0.6, 0.6), rnd(1.5, 3), rnd(-0.6, 0.6), c[0], c[1], c[2], k.life, 5);
      }
    }
  }
  function trail(from, to, kind = 'trail') {
    if (disposed || !from || !to) return;
    const k = KIND[kind] || KIND.trail;
    for (let i = 0; i < k.n; i++) {
      const u = i / k.n, life = k.life * (0.6 + u * 0.4);
      const sx = from.x, sy = from.y + 0.9 + rnd(0, 0.3), sz = from.z;
      // each particle covers the whole distance within its own lifetime
      pool.spawn(sx, sy, sz, (to.x - sx) / life, (to.y + 0.4 - sy) / life, (to.z - sz) / life,
        k.c[0], k.c[1], k.c[2], life, 0);
    }
  }

  async function enableComposer() {
    const my = ++gen;
    try {
      const base = new URL('./vendor/postprocessing/', import.meta.url).href;
      const [ec, rp, ub, sp, op] = await Promise.all(
        ['EffectComposer', 'RenderPass', 'UnrealBloomPass', 'ShaderPass', 'OutputPass'].map(n => import(base + n + '.js')));
      if (disposed || my !== gen || q !== 'high') return;
      let comp = null;
      try {
      comp = new ec.EffectComposer(renderer);
      comp.addPass(new rp.RenderPass(scene, camera));
      const strength = th && th.scene && typeof th.scene.bloom === 'number' ? th.scene.bloom : 0.5;
      bloom = new ub.UnrealBloomPass(new THREE.Vector2(size[0] || 512, size[1] || 512), strength * 0.6, 0.5, 0.85);
      comp.addPass(bloom);
      vignette = new sp.ShaderPass({
        uniforms: { tDiffuse: { value: null }, amount: { value: 0.35 } },
        vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
        fragmentShader: 'uniform sampler2D tDiffuse; uniform float amount; varying vec2 vUv;' +
          'void main(){ vec4 c = texture2D(tDiffuse, vUv); float d = distance(vUv, vec2(0.5)); c.rgb *= 1.0 - smoothstep(0.35, 0.85, d) * amount; gl_FragColor = c; }',
      });
      comp.addPass(vignette);
      comp.addPass(new op.OutputPass());
      if (size[0]) comp.setSize(size[0], size[1]);
      composer = comp;
      } catch (err) {
        if (comp) { try { comp.passes.forEach(p => p.dispose && p.dispose()); comp.renderTarget1.dispose(); comp.renderTarget2.dispose(); } catch { /* ignore */ } }
        throw err;
      }
    } catch (err) {
      if (!failed) console.warn('post-processing unavailable', err);
      failed = true;
    }
  }
  function disposeComposer() {
    gen++;
    if (!composer) return;
    try {
      for (const p of composer.passes) if (p.dispose) p.dispose();
      composer.renderTarget1.dispose(); composer.renderTarget2.dispose();
      if (composer.dispose) composer.dispose();
    } catch { /* ignore */ }
    composer = bloom = vignette = null;
  }
  if (q === 'high') enableComposer();

  return {
    burst, trail, pool,
    get composerReady() { return !!composer; },
    resize(w, h) {
      size = [w, h];
      if (composer) composer.setSize(w, h);
    },
    render(dt) {
      pool.step(dt);
      posAttr.needsUpdate = true; colAttr.needsUpdate = true;
      if (composer) {
        try { composer.render(dt); return; } catch (err) {
          console.warn('post-processing disabled', err);
          disposeComposer(); failed = true;
          try { renderer.setRenderTarget(null); } catch { /* ignore */ }
        }
      }
      renderer.render(scene, camera);
    },
    setQuality(next) {
      next = next === 'low' ? 'low' : 'high';
      if (next === q) return;
      q = next;
      pool.setCap(capFor(q));
      if (q === 'high' && !failed) enableComposer(); else disposeComposer();
    },
    setTheme(t) {
      th = t;
      if (bloom && t && t.scene && typeof t.scene.bloom === 'number') bloom.strength = t.scene.bloom * 0.6;
    },
    dispose() {
      disposed = true;
      disposeComposer();
      scene.remove(points);
      geo.dispose(); mat.dispose();
    },
  };
}
