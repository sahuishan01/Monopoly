import * as THREE from '../vendor/three.module.min.js';

const R_MIN = 5, R_MAX = 40, PHI_MIN = 0.15, PHI_MAX = 1.45;
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

// Damped orbit camera with pointer-event controls (1 pointer orbit/pan, 2 pointers pinch + pan).
// hooks: {pick(ev)->int, setHover(i), click(i)}
export function createCamera(canvas, side, hooks, opts = {}) {
  const camera = new THREE.PerspectiveCamera(45, 1, 0.1, 150);
  const goal = { theta: 0, phi: 0.9, radius: side * 1.6, target: new THREE.Vector3() };
  const cur = { theta: 0, phi: 0.9, radius: side * 1.6, target: new THREE.Vector3() };
  let reduce = !!opts.reduceMotion;
  const cleanup = [];
  const pts = new Map();
  let down = null, lastDist = 0, lastMid = null;

  function apply() {
    camera.position.set(
      cur.target.x + cur.radius * Math.sin(cur.phi) * Math.sin(cur.theta),
      cur.target.y + cur.radius * Math.cos(cur.phi),
      cur.target.z + cur.radius * Math.sin(cur.phi) * Math.cos(cur.theta));
    camera.lookAt(cur.target);
  }
  // Advance inertia; returns true while still moving.
  function tick(dt) {
    const k = reduce ? 1 : 1 - Math.exp(-Math.min(dt, 0.1) * 12);
    const d = goal.theta - cur.theta, dp = goal.phi - cur.phi, dr = goal.radius - cur.radius;
    cur.theta += d * k; cur.phi += dp * k; cur.radius += dr * k;
    cur.target.lerp(goal.target, k);
    apply();
  }
  function resize() {
    const w = canvas.clientWidth || 600, h = canvas.clientHeight || 600;
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    return [w, h];
  }
  function pan(dx, dy) {
    const k = goal.radius * 0.0018;
    goal.target.x -= (dx * Math.cos(goal.theta) + dy * Math.sin(goal.theta)) * k;
    goal.target.z -= (-dx * Math.sin(goal.theta) + dy * Math.cos(goal.theta)) * k;
    const lim = side * 0.8;
    goal.target.x = clamp(goal.target.x, -lim, lim); goal.target.z = clamp(goal.target.z, -lim, lim);
  }
  const pair = () => { const [a, b] = [...pts.values()]; return { dist: Math.hypot(a.x - b.x, a.y - b.y), mx: (a.x + b.x) / 2, my: (a.y + b.y) / 2 }; };

  const on = (e, f, o) => { canvas.addEventListener(e, f, o); cleanup.push(() => canvas.removeEventListener(e, f, o)); };
  on('contextmenu', e => e.preventDefault());
  on('pointerdown', e => {
    try { canvas.setPointerCapture(e.pointerId); } catch { }
    pts.set(e.pointerId, { x: e.clientX, y: e.clientY, btn: e.button });
    if (pts.size === 1) down = { x: e.clientX, y: e.clientY, moved: false };
    else { if (down) down.moved = true; const p = pair(); lastDist = p.dist; lastMid = { x: p.mx, y: p.my }; }
  });
  on('pointermove', e => {
    const p = pts.get(e.pointerId);
    if (!p) { hooks.setHover(hooks.pick(e)); return; }
    const dx = e.clientX - p.x, dy = e.clientY - p.y;
    p.x = e.clientX; p.y = e.clientY;
    if (down && Math.hypot(e.clientX - down.x, e.clientY - down.y) > 4) down.moved = true;
    if (pts.size >= 2) {
      const q = pair();
      if (lastDist > 0 && q.dist > 0) goal.radius = clamp(goal.radius * lastDist / q.dist, R_MIN, R_MAX);
      if (lastMid) pan(q.mx - lastMid.x, q.my - lastMid.y);
      lastDist = q.dist; lastMid = { x: q.mx, y: q.my };
    } else if (p.btn === 2 || e.shiftKey) {
      pan(dx, dy);
    } else {
      goal.theta -= dx * 0.008;
      goal.phi = clamp(goal.phi - dy * 0.008, PHI_MIN, PHI_MAX);
    }
  });
  const up = e => {
    const had = pts.delete(e.pointerId);
    lastDist = 0; lastMid = null;
    if (had && pts.size === 0) {
      if (e.type === 'pointerup' && down && !down.moved) {
        const i = hooks.pick(e);
        if (i >= 0) hooks.click(i);
      }
      down = null;
    }
  };
  on('pointerup', up);
  on('pointercancel', up);
  on('pointerleave', () => hooks.setHover(-1));
  on('wheel', e => {
    e.preventDefault();
    goal.radius = clamp(goal.radius * (1 + Math.sign(e.deltaY) * 0.1), R_MIN, R_MAX);
  }, { passive: false });
  on('dblclick', () => { goal.theta = 0; goal.phi = 0.9; goal.radius = side * 1.6; goal.target.set(0, 0, 0); });
  apply();
  return {
    camera, tick, resize,
    setReduceMotion(v) { reduce = !!v; },
    dispose() { cleanup.forEach(f => f()); cleanup.length = 0; pts.clear(); down = null; },
  };
}
