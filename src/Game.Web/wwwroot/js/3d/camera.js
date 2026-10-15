import * as THREE from '../vendor/three.module.min.js';

// Orbit camera + pointer controls. hooks: {pick(ev)->int, setHover(i), click(i)}
export function createCamera(canvas, side, hooks) {
  const camera = new THREE.PerspectiveCamera(45, 1, 0.1, 100);
  const orbit = { theta: 0, phi: 0.9, radius: side * 1.6, target: new THREE.Vector3(0, 0, 0) };
  let down = null;
  const cleanup = [];
  function update() {
    const o = orbit;
    camera.position.set(
      o.target.x + o.radius * Math.sin(o.phi) * Math.sin(o.theta),
      o.target.y + o.radius * Math.cos(o.phi),
      o.target.z + o.radius * Math.sin(o.phi) * Math.cos(o.theta));
    camera.lookAt(o.target);
  }
  function resize() {
    const w = canvas.clientWidth || 600, h = canvas.clientHeight || 600;
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    return [w, h];
  }
  const pts = new Map();
  let lastDist = 0;
  const on = (e, f, o) => { canvas.addEventListener(e, f, o); cleanup.push(() => canvas.removeEventListener(e, f, o)); };
  on('contextmenu', e => e.preventDefault());
  on('pointerdown', e => {
    canvas.setPointerCapture(e.pointerId);
    pts.set(e.pointerId, { x: e.clientX, y: e.clientY, btn: e.button });
    down = { x: e.clientX, y: e.clientY, moved: false };
    lastDist = 0;
  });
  on('pointermove', e => {
    const p = pts.get(e.pointerId);
    if (!p) { hooks.setHover(hooks.pick(e)); return; }
    const dx = e.clientX - p.x, dy = e.clientY - p.y;
    if (down && Math.hypot(e.clientX - down.x, e.clientY - down.y) > 4) down.moved = true;
    if (pts.size === 2) {
      pts.set(e.pointerId, { ...p, x: e.clientX, y: e.clientY });
      const [c, d] = [...pts.values()];
      const dist = Math.hypot(c.x - d.x, c.y - d.y);
      if (lastDist) orbit.radius = Math.min(40, Math.max(5, orbit.radius * lastDist / dist));
      lastDist = dist;
    } else if (p.btn === 2 || e.shiftKey) {
      const k = orbit.radius * 0.0018;
      orbit.target.x -= (dx * Math.cos(orbit.theta) + dy * Math.sin(orbit.theta)) * k;
      orbit.target.z -= (-dx * Math.sin(orbit.theta) + dy * Math.cos(orbit.theta)) * k;
      pts.set(e.pointerId, { ...p, x: e.clientX, y: e.clientY });
    } else {
      orbit.theta -= dx * 0.008;
      orbit.phi = Math.min(1.45, Math.max(0.15, orbit.phi - dy * 0.008));
      pts.set(e.pointerId, { ...p, x: e.clientX, y: e.clientY });
    }
    update();
  });
  const up = e => {
    pts.delete(e.pointerId);
    if (e.type === 'pointerup' && down && !down.moved) {
      const i = hooks.pick(e);
      if (i >= 0) hooks.click(i);
    }
    down = null;
  };
  on('pointerup', up);
  on('pointercancel', up);
  on('pointerleave', () => hooks.setHover(-1));
  on('wheel', e => {
    e.preventDefault();
    orbit.radius = Math.min(40, Math.max(5, orbit.radius * (1 + Math.sign(e.deltaY) * 0.1)));
    update();
  }, { passive: false });
  on('dblclick', () => { orbit.theta = 0; orbit.phi = 0.9; orbit.radius = side * 1.6; orbit.target.set(0, 0, 0); update(); });
  update();
  return { camera, update, resize, dispose() { cleanup.forEach(f => f()); cleanup.length = 0; } };
}
