// 2D board effects: pawn hops (Web Animations API), tile pulses/sweeps and a canvas particle layer
// (coins, build dust, confetti). Display state rendered by Blazor stays the source of truth: every
// animation here is transient (fill: 'none') and snap() cancels them all.
//
// create(root, optsJson) returns an instance; Board2D.razor calls its methods through IJSObjectReference.

export const CAP_LOW = 40;
export const CAP_SMALL = 160;
export const CAP_HIGH = 320;

export function capFor(quality, small) {
  if (quality === 'low') return CAP_LOW;
  if (quality === 'high') return CAP_HIGH;
  return small ? CAP_SMALL : CAP_HIGH;
}

// Per-hop duration so that a whole walk fits roughly in the pacer's move cue (0.7 s).
export function hopMs(hops) {
  if (hops <= 0) return 0;
  return Math.max(70, Math.min(150, 760 / hops));
}

// Point on a quadratic bezier from a to b bulging "up" by lift (all in px).
export function arc(a, b, lift, t) {
  const mx = (a[0] + b[0]) / 2, my = (a[1] + b[1]) / 2 - lift;
  const u = 1 - t;
  return [u * u * a[0] + 2 * u * t * mx + t * t * b[0], u * u * a[1] + 2 * u * t * my + t * t * b[1]];
}

const parse = j => (typeof j === 'string' ? JSON.parse(j) : j) || {};
const ease = t => 1 - Math.pow(1 - t, 3);
const hsl = (h, s = 80, l = 60, a = 1) => `hsl(${h} ${s}% ${l}% / ${a})`;

class Fx {
  constructor(root, opts) {
    this.root = root;
    this.grid = root.querySelector('.board2d');
    this.layer = root.querySelector('.pawns');
    this.opts = { reduceMotion: false, quality: 'auto', currency: '' };
    this.parts = [];
    this.anims = new Set();
    this.raf = 0;
    this.last = 0;
    this.dead = false;
    this.mq = typeof matchMedia === 'function' ? matchMedia('(prefers-reduced-motion: reduce)') : null;
    this.small = typeof matchMedia === 'function' && matchMedia('(max-width: 700px)').matches;

    const c = this.canvas = document.createElement('canvas');
    c.className = 'b2d-fx';
    c.setAttribute('aria-hidden', 'true');
    root.appendChild(c);
    this.ctx = c.getContext('2d');
    this.resize = () => this.fit();
    this.ro = typeof ResizeObserver === 'function' ? new ResizeObserver(this.resize) : null;
    this.ro?.observe(root);
    this.onVis = () => { if (document.hidden) this.clearParticles(); };
    document.addEventListener('visibilitychange', this.onVis);
    this.setOptions(opts);
    this.fit();
  }

  setOptions(json) {
    Object.assign(this.opts, parse(json));
    this.root.classList.toggle('rm', !!this.opts.reduceMotion);
    if (this.reduced()) this.snap();
  }

  reduced() { return !!this.opts.reduceMotion || !!this.mq?.matches; }
  cap() { return capFor(this.opts.quality, this.small); }
  canAnimate() { return !this.dead && !this.reduced() && !document.hidden; }

  fit() {
    if (this.dead) return;
    const r = this.root.getBoundingClientRect();
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    this.dpr = dpr;
    this.canvas.width = Math.max(1, Math.round(r.width * dpr));
    this.canvas.height = Math.max(1, Math.round(r.height * dpr));
  }

  // Board-percentage point -> canvas CSS pixels.
  px(p) {
    const g = this.grid.getBoundingClientRect(), r = this.root.getBoundingClientRect();
    return [g.left - r.left + p[0] / 100 * g.width, g.top - r.top + p[1] / 100 * g.height];
  }

  track(a) {
    this.anims.add(a);
    const drop = () => this.anims.delete(a);
    a.finished.then(drop, drop);
    return a;
  }

  tileEl(i) { return this.grid?.querySelector(`[data-tile="${i}"]`); }

  // points: [[x%, y%], ...] from the pawn's current spot through each hop to its final spot.
  // Resolves when the walk ends or is cancelled. Blazor keeps the pawn drawn at its origin until then; on the
  // way out the final spot is written inline in the same task the animation is cancelled, so no frame shows
  // the pawn anywhere but on its path.
  hop(player, points, tile, hue, jump) {
    return new Promise(resolve => {
      const el = this.layer?.querySelector(`[data-pawn="${player}"]`);
      if (!el || !this.canAnimate() || !Array.isArray(points) || points.length < 2) { resolve(false); return; }
      for (const a of el.getAnimations()) a.cancel();
      const body = el.querySelector('.pawn-body');
      for (const a of body?.getAnimations() || []) a.cancel();
      const hops = points.length - 1;
      const per = jump ? 520 : hopMs(hops);
      const dur = per * hops;
      const pos = [], arcs = [];
      points.forEach((p, i) => {
        const off = i / hops;
        pos.push({ left: p[0] + '%', top: p[1] + '%', offset: off, easing: 'cubic-bezier(.45,.05,.55,.95)' });
        if (i < hops) {
          arcs.push({ transform: 'translateY(0) scale(1, 1)', offset: off, easing: 'cubic-bezier(.2,.6,.4,1)' });
          arcs.push({ transform: `translateY(${jump ? -150 : -55}%) scale(.94, 1.08)`, offset: (i + .5) / hops, easing: 'cubic-bezier(.6,0,.8,.4)' });
        }
      });
      arcs.push({ transform: 'translateY(0) scale(1, 1)', offset: 1 });
      const last = points[hops];
      const a = this.track(el.animate(pos, { duration: dur, fill: 'forwards' }));
      const b = body ? this.track(body.animate(arcs, { duration: dur, fill: 'none' })) : null;
      let done = false;
      const finish = ok => {
        if (done) return;
        done = true;
        clearTimeout(guard);
        if (!this.dead) {
          el.style.transition = 'none';
          el.style.left = last[0] + '%';
          el.style.top = last[1] + '%';
          try { a.cancel(); } catch { }
          void el.offsetWidth;
          el.style.transition = '';
        }
        if (ok && !this.dead && body) {
          this.track(body.animate([
            { transform: 'scale(1.18, .78)' }, { transform: 'scale(.94, 1.08)' }, { transform: 'scale(1, 1)' },
          ], { duration: 260, easing: 'ease-out' }));
          this.pulse(tile, hue);
        }
        resolve(ok);
      };
      const guard = setTimeout(() => { try { b?.cancel(); } catch { } finish(false); }, dur + 1500);
      a.finished.then(() => finish(true), () => finish(false));
    });
  }

  pulse(tile, hue) {
    const t = this.tileEl(tile);
    if (!t || !this.canAnimate()) return;
    const c = hsl(hue ?? 160, 85, 62, .85), z = hsl(hue ?? 160, 85, 62, 0);
    this.track(t.animate([
      { boxShadow: `0 0 0 0 ${c}, 0 0 0 0 ${c}`, filter: 'brightness(1.5)' },
      { boxShadow: `0 0 0 5px ${z}, 0 0 24px 4px ${z}`, filter: 'brightness(1)' },
    ], { duration: 650, easing: 'ease-out' }));
  }

  // Ownership change: a coloured sheen sweeps across the tile.
  sweep(tile, hue) {
    const t = this.tileEl(tile);
    if (!t || !this.canAnimate()) return;
    const bg = `linear-gradient(100deg, transparent 15%, ${hsl(hue, 85, 62, .85)} 50%, transparent 85%)`;
    this.track(t.animate([
      { transform: 'translateX(-120%)', opacity: 1, background: bg },
      { transform: 'translateX(120%)', opacity: 1, background: bg },
    ], { duration: 700, easing: 'cubic-bezier(.3,.1,.3,1)', pseudoElement: '::after' }));
    this.track(t.animate([{ scale: '1' }, { scale: '1.12' }, { scale: '1' }], { duration: 450, easing: 'ease-out' }));
  }

  token(name, fallback) {
    const v = getComputedStyle(this.root).getPropertyValue(name).trim();
    return v || fallback;
  }

  room(n) { return Math.max(0, Math.min(n, this.cap() - this.parts.length)); }

  coins(from, to, count, amount) {
    if (!this.canAnimate()) return;
    const a = this.px(from), b = this.px(to);
    const n = this.room(count | 0);
    const gold = this.token('--coin', '#f5c542'), edge = this.token('--coin-edge', '#a8741a');
    const size = Math.max(3, this.grid.getBoundingClientRect().width / 110);
    const lift = Math.max(30, Math.hypot(b[0] - a[0], b[1] - a[1]) * .35);
    for (let i = 0; i < n; i++) {
      this.parts.push({
        k: 'coin', a: [a[0] + (Math.random() - .5) * size * 3, a[1] + (Math.random() - .5) * size * 3], b,
        lift: lift * (.7 + Math.random() * .6), t: -i * 0.035, dur: .75 + Math.random() * .2, r: size * (.85 + Math.random() * .3),
        spin: Math.random() * 6, gold, edge,
      });
    }
    if (amount > 0) this.popup(to, `${this.opts.currency || ''}${amount}`);
    this.kick();
  }

  popup(at, text) {
    if (!this.canAnimate()) return;
    const [x, y] = this.px(at);
    const d = document.createElement('span');
    d.className = 'b2d-pop';
    d.textContent = '+' + text;
    d.style.left = x + 'px'; d.style.top = y + 'px';
    this.root.appendChild(d);
    const a = this.track(d.animate([
      { transform: 'translate(-50%, -40%) scale(.6)', opacity: 0 },
      { transform: 'translate(-50%, -140%) scale(1.05)', opacity: 1, offset: .25 },
      { transform: 'translate(-50%, -260%) scale(1)', opacity: 0 },
    ], { duration: 1300, delay: 450, easing: 'ease-out', fill: 'both' }));
    const rm = () => d.remove();
    a.finished.then(rm, rm);
  }

  dust(at) {
    if (!this.canAnimate()) return;
    const [x, y] = this.px(at);
    const n = this.room(18);
    const col = this.token('--dust', 'rgba(220,220,230,.8)');
    const s = this.grid.getBoundingClientRect().width / 60;
    for (let i = 0; i < n; i++) {
      const ang = Math.random() * Math.PI * 2, sp = s * (2 + Math.random() * 3);
      this.parts.push({ k: 'dust', x, y, vx: Math.cos(ang) * sp, vy: Math.sin(ang) * sp - s * 2, t: 0, dur: .6 + Math.random() * .4, r: s * (.3 + Math.random() * .5), col });
    }
    for (let i = 0; i < Math.min(8, this.room(8)); i++) {
      const ang = -Math.PI / 2 + (Math.random() - .5) * 1.6, sp = s * (6 + Math.random() * 5);
      this.parts.push({ k: 'spark', x, y, vx: Math.cos(ang) * sp, vy: Math.sin(ang) * sp, t: 0, dur: .5 + Math.random() * .3, r: s * .25, col: this.token('--accent', '#fff') });
    }
    this.kick();
  }

  confetti() {
    if (!this.canAnimate()) return;
    const r = this.root.getBoundingClientRect();
    const n = this.room(160);
    const cols = [this.token('--accent', '#2fc'), this.token('--glow', '#a7f'), this.token('--coin', '#fc4')];
    for (let i = 0; i < 6; i++) cols.push(hsl(i * 61.2, 85, 62));
    for (let i = 0; i < n; i++) {
      const side = i % 2 ? 1 : 0;
      this.parts.push({
        k: 'conf', x: side ? r.width * (.85 + Math.random() * .15) : r.width * Math.random() * .15, y: r.height * (.75 + Math.random() * .25),
        vx: (side ? -1 : 1) * r.width * (.25 + Math.random() * .45), vy: -r.height * (.9 + Math.random() * .7),
        t: -Math.random() * .4, dur: 2.6 + Math.random() * 1.2, w: 4 + Math.random() * 5, h: 7 + Math.random() * 7,
        rot: Math.random() * 6, vr: (Math.random() - .5) * 14, col: cols[i % cols.length],
      });
    }
    this.kick();
  }

  kick() {
    if (this.raf || !this.parts.length || this.dead) return;
    this.last = performance.now();
    this.raf = requestAnimationFrame(t => this.frame(t));
  }

  frame(now) {
    this.raf = 0;
    if (this.dead) return;
    if (document.hidden) { this.clearParticles(); return; }
    const dt = Math.min(.05, Math.max(0, (now - this.last) / 1000));
    this.last = now;
    const g = this.ctx, dpr = this.dpr || 1;
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.clearRect(0, 0, this.canvas.width, this.canvas.height);
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    const keep = [];
    for (const p of this.parts) {
      p.t += dt;
      if (p.t < 0) { keep.push(p); continue; }
      const life = p.t / p.dur;
      if (life >= 1) continue;
      keep.push(p);
      if (p.k === 'coin') {
        const [x, y] = arc(p.a, p.b, p.lift, ease(life));
        const w = Math.abs(Math.cos(p.spin + p.t * 14));
        g.globalAlpha = life > .85 ? (1 - life) / .15 : 1;
        g.fillStyle = p.edge;
        g.beginPath(); g.ellipse(x, y + p.r * .18, p.r * Math.max(.2, w), p.r, 0, 0, Math.PI * 2); g.fill();
        g.fillStyle = p.gold;
        g.beginPath(); g.ellipse(x, y, p.r * Math.max(.15, w) * .9, p.r * .9, 0, 0, Math.PI * 2); g.fill();
        g.fillStyle = 'rgba(255,255,255,.7)';
        g.beginPath(); g.ellipse(x - p.r * .25 * w, y - p.r * .3, p.r * .22 * w, p.r * .22, 0, 0, Math.PI * 2); g.fill();
      } else if (p.k === 'dust' || p.k === 'spark') {
        p.vx *= 1 - 2.5 * dt; p.vy = p.vy * (1 - 2.5 * dt) + (p.k === 'spark' ? 60 : -8) * dt;
        p.x += p.vx * dt; p.y += p.vy * dt;
        g.globalAlpha = (1 - life) * (p.k === 'spark' ? 1 : .55);
        g.fillStyle = p.col;
        g.beginPath(); g.arc(p.x, p.y, p.r * (p.k === 'dust' ? 1 + life * 1.8 : 1), 0, Math.PI * 2); g.fill();
      } else {
        p.vy += 900 * dt; p.vx *= 1 - 1.2 * dt; p.vy *= 1 - 1.2 * dt;
        p.x += p.vx * dt; p.y += p.vy * dt; p.rot += p.vr * dt;
        g.globalAlpha = life > .8 ? (1 - life) / .2 : 1;
        g.save(); g.translate(p.x, p.y); g.rotate(p.rot); g.scale(1, Math.cos(p.rot * 1.7));
        g.fillStyle = p.col; g.fillRect(-p.w / 2, -p.h / 2, p.w, p.h); g.restore();
      }
    }
    g.globalAlpha = 1;
    this.parts = keep;
    if (keep.length) this.raf = requestAnimationFrame(t => this.frame(t));
    else g.clearRect(0, 0, this.canvas.width, this.canvas.height);
  }

  clearParticles() {
    this.parts = [];
    if (this.raf) cancelAnimationFrame(this.raf);
    this.raf = 0;
    this.ctx?.clearRect(0, 0, this.canvas.width, this.canvas.height);
  }

  // State and events disagree (snapshot/resync) or motion was disabled: drop every transient effect.
  snap() {
    for (const a of [...this.anims]) { try { a.cancel(); } catch { } }
    this.anims.clear();
    this.root.querySelectorAll('.b2d-pop').forEach(e => e.remove());
    this.clearParticles();
  }

  activeCount() { return { anims: this.anims.size, particles: this.parts.length, looping: !!this.raf }; }

  dispose() {
    if (this.dead) return;
    this.snap();
    this.dead = true;
    this.ro?.disconnect();
    document.removeEventListener('visibilitychange', this.onVis);
    this.canvas.remove();
  }
}

export function create(root, optsJson) {
  return new Fx(root, optsJson);
}
