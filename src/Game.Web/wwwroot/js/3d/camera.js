import * as THREE from '../vendor/three.module.min.js';

const R_MIN = 5, PHI_MIN = 0.15, PHI_MAX = 1.45;
const FOLLOW = 0.85;            // follow-cam distance as a fraction of the fitted overview distance (mild zoom, little cropping)
const MARGIN = 0.92;            // fitted board stays inside this fraction of the viewport (NDC)
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

// Default polar angle: a little more top-down on tall (portrait) canvases so the board uses the height.
export function defaultPhi(aspect) { return aspect < 0.85 ? 0.72 : 0.78; }

/**
 * Smallest orbit radius at which the whole board (a box of half-width `half` and height `top`,
 * centred on the origin) fits inside the viewport for this aspect, fov and polar angle.
 */
export function fitRadius(half, top, aspect, fov, phi, margin = MARGIN) {
  const probe = new THREE.PerspectiveCamera(fov, aspect, 0.1, 1000);
  const v = new THREE.Vector3(), origin = new THREE.Vector3();
  const pts = [];
  for (const x of [-half, half]) for (const y of [0, top]) for (const z of [-half, half]) pts.push(new THREE.Vector3(x, y, z));
  const fits = r => {
    probe.position.set(0, r * Math.cos(phi), r * Math.sin(phi));
    probe.lookAt(origin);
    probe.updateMatrixWorld();
    for (const p of pts) {
      v.copy(p).project(probe);
      if (v.z > 1 || v.z < -1 || Math.abs(v.x) > margin || Math.abs(v.y) > margin) return false;
    }
    return true;
  };
  let lo = 1, hi = 400;
  for (let i = 0; i < 30; i++) { const mid = (lo + hi) / 2; if (fits(mid)) hi = mid; else lo = mid; }
  return hi;
}

// Damped orbit camera with pointer-event controls (1 pointer orbit/pan, 2 pointers pinch + pan).
// The overview distance is fitted to the canvas aspect so the whole board is visible in portrait and
// landscape, and re-fitted on resize unless the user has zoomed (an orbit drag keeps its angle too).
// hooks: {pick(ev)->int, setHover(i), click(i)}
export function createCamera(canvas, side, hooks, opts = {}) {
  const camera = new THREE.PerspectiveCamera(45, 1, 0.1, 400);
  const half = side * 0.6 + 0.65, top = 1.2;   // tile centres span ±side*0.6, plus half a tile and the bevel
  let fitR = side * 1.6, phi0 = 0.9, rMax = 40;
  const goal = { theta: 0, phi: phi0, radius: fitR, target: new THREE.Vector3() };
  const cur = { theta: 0, phi: phi0, radius: fitR, target: new THREE.Vector3() };
  let reduce = !!opts.reduceMotion;
  let desired = null, manualT = 0, fitted = false, zoomed = false, orbited = false;
  const touch = () => { manualT = 4; };
  const cleanup = [];
  const pts = new Map();
  let down = null, lastDist = 0, lastMid = null, tap = false;

  function apply() {
    camera.position.set(
      cur.target.x + cur.radius * Math.sin(cur.phi) * Math.sin(cur.theta),
      cur.target.y + cur.radius * Math.cos(cur.phi),
      cur.target.z + cur.radius * Math.sin(cur.phi) * Math.cos(cur.theta));
    camera.lookAt(cur.target);
  }
  const autoRadius = () => desired && desired.follow ? fitR * FOLLOW : fitR;
  // Advance inertia; returns true while still moving.
  function tick(dt) {
    dt = Number.isFinite(dt) ? Math.max(0, dt) : 0;
    if (pts.size > 0) manualT = 4;
    if (manualT > 0) {
      manualT -= dt;
      if (manualT <= 0 && desired) { goal.target.copy(desired.target); if (!zoomed) goal.radius = autoRadius(); }
    }
    const k = reduce ? 1 : 1 - Math.exp(-Math.min(dt, 0.1) * 12);
    const d = goal.theta - cur.theta, dp = goal.phi - cur.phi, dr = goal.radius - cur.radius;
    cur.theta += d * k; cur.phi += dp * k; cur.radius += dr * k;
    cur.target.lerp(goal.target, k);
    apply();
  }
  // Re-fit the overview distance to the canvas aspect. The first fit snaps; later ones ease.
  function refit(aspect) {
    phi0 = defaultPhi(aspect);
    fitR = fitRadius(half, top, aspect, camera.fov, phi0);
    rMax = Math.max(40, fitR * 1.6);
    if (!orbited) goal.phi = phi0;
    if (!zoomed && manualT <= 0 && pts.size === 0) goal.radius = autoRadius();
    goal.radius = clamp(goal.radius, R_MIN, rMax);
    if (!fitted) { fitted = true; cur.phi = goal.phi; cur.radius = goal.radius; apply(); }
  }
  function resize() {
    const w = canvas.clientWidth || 600, h = canvas.clientHeight || 600;
    if (!fitted || Math.abs(w / h - camera.aspect) > 1e-6) {
      camera.aspect = w / h;
      camera.updateProjectionMatrix();
      refit(camera.aspect);
    }
    return [w, h];
  }
  function reset() {
    zoomed = orbited = false;
    desired = { target: new THREE.Vector3(), follow: false };
    goal.theta = 0; goal.phi = phi0; goal.radius = fitR; goal.target.set(0, 0, 0);
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
    touch();
    // Freeze any easing under the finger: a tap selects what was on screen when it landed, even on a slow
    // device where the follow cam would otherwise keep gliding between pointerdown and the click.
    goal.theta = cur.theta; goal.phi = cur.phi; goal.radius = cur.radius; goal.target.copy(cur.target);
    // A new primary pointer means no other pointer is down: drop any that never reported up/cancel
    // (e.g. a touch whose end went to an overlay), or a stale entry would block taps and auto-camera.
    if (e.isPrimary) pts.clear();
    try { canvas.setPointerCapture(e.pointerId); } catch { }
    pts.set(e.pointerId, { x: e.clientX, y: e.clientY, btn: e.button });
    tap = false;
    if (pts.size === 1) down = { x: e.clientX, y: e.clientY, moved: false };
    else { if (down) down.moved = true; const p = pair(); lastDist = p.dist; lastMid = { x: p.mx, y: p.my }; }
  });
  on('pointermove', e => {
    const p = pts.get(e.pointerId);
    if (p) touch();
    if (!p) { hooks.setHover(hooks.pick(e)); return; }
    const dx = e.clientX - p.x, dy = e.clientY - p.y;
    p.x = e.clientX; p.y = e.clientY;
    if (down && Math.hypot(e.clientX - down.x, e.clientY - down.y) > 4) down.moved = true;
    if (pts.size >= 2) {
      const q = pair();
      if (lastDist > 0 && q.dist > 0) { goal.radius = clamp(goal.radius * lastDist / q.dist, R_MIN, rMax); zoomed = true; }
      if (lastMid) pan(q.mx - lastMid.x, q.my - lastMid.y);
      lastDist = q.dist; lastMid = { x: q.mx, y: q.my };
    } else if (p.btn === 2 || e.shiftKey) {
      pan(dx, dy);
    } else if (down && down.moved) {
      goal.theta -= dx * 0.008;
      goal.phi = clamp(goal.phi - dy * 0.008, PHI_MIN, PHI_MAX);
      orbited = true;
    }
  });
  const up = e => {
    const had = pts.delete(e.pointerId);
    lastDist = 0; lastMid = null;
    if (had && pts.size === 0) {
      tap = e.type === 'pointerup' && down && !down.moved;
      down = null;
    }
  };
  on('pointerup', up);
  // Select on the click that follows a tap, not on pointerup: a touch tap's synthetic click would otherwise
  // land on the card's backdrop that the selection just opened, and close it again at once.
  on('click', e => {
    if (!tap) return;
    tap = false;
    const i = hooks.pick(e);
    if (i >= 0) hooks.click(i);
  });
  on('pointercancel', up);
  on('lostpointercapture', e => { if (pts.has(e.pointerId)) up(e); });
  on('pointerleave', () => hooks.setHover(-1));
  on('wheel', e => {
    e.preventDefault();
    touch();
    zoomed = true;
    goal.radius = clamp(goal.radius * (1 + Math.sign(e.deltaY) * 0.1), R_MIN, rMax);
  }, { passive: false });
  on('dblclick', () => { manualT = 0; reset(); });
  apply();
  return {
    camera, tick, resize, reset,
    // follow/overview are auto modes; a user drag holds them off for 4 s, a user zoom keeps its distance
    follow(p) {
      desired = { target: new THREE.Vector3(p.x, 0, p.z).multiplyScalar(0.6), follow: true };
      if (manualT <= 0 && pts.size === 0) { goal.target.copy(desired.target); if (!zoomed) goal.radius = autoRadius(); }
    },
    overview() {
      desired = { target: new THREE.Vector3(), follow: false };
      if (manualT <= 0 && pts.size === 0) { goal.target.copy(desired.target); if (!zoomed) goal.radius = autoRadius(); }
    },
    get manual() { return manualT > 0; },
    get distance() { return cur.radius; },
    get settled() {
      return Math.abs(goal.theta - cur.theta) < 1e-3 && Math.abs(goal.phi - cur.phi) < 1e-3
        && Math.abs(goal.radius - cur.radius) < 1e-2 && goal.target.distanceTo(cur.target) < 1e-2;
    },
    get fit() { return { radius: fitR, phi: phi0, half, top }; },
    setReduceMotion(v) { reduce = !!v; },
    dispose() { cleanup.forEach(f => f()); cleanup.length = 0; pts.clear(); down = null; },
  };
}
