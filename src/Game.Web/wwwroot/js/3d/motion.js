// Pure motion engine (no THREE import): one update list stepped from the render loop.
export const ease = {
  linear: t => t,
  outCubic: t => 1 - Math.pow(1 - t, 3),
  inOutQuad: t => (t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2),
  outBack: t => { const c1 = 1.70158, c3 = c1 + 1; return 1 + c3 * Math.pow(t - 1, 3) + c1 * Math.pow(t - 1, 2); },
};

export class Tween {
  constructor(duration, easing, onUpdate, onDone) {
    this.duration = Math.max(0, duration); this.easing = easing || ease.linear;
    this.onUpdate = onUpdate || null; this.onDone = onDone || null;
    this.t = 0; this.done = false; this.key = null;
  }
  step(dt) {
    if (this.done) return;
    this.t += dt;
    const u = this.duration <= 0 ? 1 : Math.min(1, this.t / this.duration);
    if (this.onUpdate) this.onUpdate(this.easing(u), u);
    if (u >= 1) this.finish(true);
  }
  finish(ran) {
    if (this.done) return;
    if (!ran && this.onUpdate) this.onUpdate(this.easing(1), 1);
    this.done = true;
    if (this.onDone) this.onDone();
  }
}

// Tiles walked from `from` to `to` inclusive of `to`, exclusive of `from`; wraps at n.
// Long distances (teleports, going back) return a single slide step.
export function hopPath(from, to, n, maxWalk = 12) {
  if (from === to || n <= 0) return [to];
  const d = (((to - from) % n) + n) % n;
  if (d > maxWalk) return [to];
  const out = [];
  for (let i = 1; i <= d; i++) out.push((from + i) % n);
  return out;
}

// Rest rotation (Euler XYZ) that brings the face showing `value` to +y.
// BoxGeometry face order: +x,-x,+y,-y,+z,-z showing 3,4,1,6,2,5.
export const DICE_FACE_VALUES = [3, 4, 1, 6, 2, 5];
const H = Math.PI / 2;
const REST = { 3: [0, 0, H], 4: [0, 0, -H], 1: [0, 0, 0], 6: [Math.PI, 0, 0], 2: [-H, 0, 0], 5: [H, 0, 0] };
export function diceRest(v) { return REST[v] || REST[1]; }

export class Motion {
  // opts: {posOf(tile)->{x,y,z}, n: board length, reduce}
  constructor(opts = {}) {
    this.posOf = opts.posOf; this.n = opts.n || 40; this.reduce = !!opts.reduce;
    this.list = [];
  }
  setReduce(v) { this.reduce = !!v; if (this.reduce) this.flush(); }
  add(tw, key) {
    if (key) { this.cancel(key); tw.key = key; }
    if (this.reduce) { tw.finish(false); return tw; }
    this.list.push(tw);
    return tw;
  }
  cancel(key) {
    for (const t of this.list) if (t.key === key) t.done = true;
    this.list = this.list.filter(t => !t.done);
  }
  // Complete every pending tween immediately.
  flush() {
    for (let guard = 0; this.list.length && guard < 1000; guard++) {
      const cur = this.list; this.list = [];
      for (const t of cur) t.finish(false);
    }
  }
  update(dt) {
    const d = Math.min(Math.max(dt, 0), 0.05);
    const cur = this.list;
    for (const t of cur) t.step(d);
    // tweens added during step were pushed onto this.list (same array) - keep them
    this.list = this.list.filter(t => !t.done);
  }
  clear() { this.list = []; }
  get active() { return this.list.length; }

  hop(token, fromTile, toTile, perStep = 0.18, onDone) {
    const m = token.mesh, base = token.baseY ?? 0.35;
    const path = hopPath(fromTile, toTile, this.n);
    const key = token;
    const slide = path.length === 1 && ((((toTile - fromTile) % this.n) + this.n) % this.n) > 12;
    let from = m.position.clone ? m.position.clone() : { ...m.position };
    token.busy = true;
    const finish = () => { token.busy = false; if (onDone) onDone(); };
    const next = i => {
      if (i >= path.length) {
        const fin = this.posOf(toTile); m.position.set(fin.x, base, fin.z);
        this.add(new Tween(0.14, ease.outCubic, u => { m.scale.set(1 + 0.2 * (1 - u), 1 - 0.25 * (1 - u), 1 + 0.2 * (1 - u)); },
          () => { m.scale.set(1, 1, 1); finish(); }), key);
        return;
      }
      const a = { x: from.x, z: from.z }, b = this.posOf(path[i]);
      const dur = slide ? perStep * 2.5 : perStep, lift = slide ? 0.6 : 0.35;
      this.add(new Tween(dur, ease.linear, (e, u) => {
        m.position.set(a.x + (b.x - a.x) * u, base + Math.sin(Math.PI * u) * lift, a.z + (b.z - a.z) * u);
      }, () => { from = { x: b.x, z: b.z }; next(i + 1); }), key);
    };
    next(0);
  }

  spring(mesh, from, to, dur = 0.45) {
    const f = from, t = to;
    return this.add(new Tween(dur, ease.outBack, e => {
      const s = f + (t - f) * e; mesh.scale.set(s, s, s);
    }, () => mesh.scale.set(t, t, t)), mesh);
  }

  // group: {visible, ...}; cubes: [mesh0, mesh1]; d1,d2: 1..6
  dice(group, cubes, d1, d2) {
    const vals = [d1, d2], key = group;
    group.visible = true;
    cubes.forEach((c, i) => {
      const r = diceRest(vals[i]), spin = [2 + i, 3, 2 + (i ? 0 : 1)];
      const x = (i ? 0.55 : -0.55);
      c.userData.rest = r;
      this.add(new Tween(0.8, ease.outCubic, (e, u) => {
        const k = 1 - e;
        c.rotation.set(r[0] + spin[0] * 2 * Math.PI * k, r[1] + spin[1] * 2 * Math.PI * k, r[2] + spin[2] * 2 * Math.PI * k);
        c.position.set(x, 0.5 + Math.abs(Math.sin(u * Math.PI * 2.5)) * 1.2 * (1 - u) + 0.0, 0);
      }, () => { c.rotation.set(r[0], r[1], r[2]); c.position.set(x, 0.5, 0); }), c);
    });
    // linger then hide
    this.add(new Tween(1.8, ease.linear, null, () => { group.visible = false; }), key);
  }
}
