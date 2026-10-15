// Shared device rules for animated backdrops. Pure functions (testable in node) plus a browser probe.
//
// autoTier: what Auto quality resolves to before any frame-time probe (board3d.js uses the same rule).
// heroMode: how the Home hero is drawn
//   'gl'    animated three.js demo board
//   'css'   CSS/SVG mini-board, tiles lighting up and dice floating (no GL)
//   'still' the CSS mini-board without animation (reduced motion)

export const NARROW_PX = 700;
export const LOW_CORES = 4;

export function autoTier({ width = 1280, cores = 8 } = {}) {
  return width <= NARROW_PX || (cores || 8) <= LOW_CORES ? 'low' : 'high';
}

export function heroMode({ quality = 'auto', reduceMotion = false, prefersReduced = false, width = 1280, cores = 8, webgl = true } = {}) {
  if (reduceMotion || prefersReduced) return 'still';
  if (!webgl || quality === 'low') return 'css';
  if (quality === 'high') return 'gl';
  return autoTier({ width, cores }) === 'low' ? 'css' : 'gl';
}

/** Browser facts for heroMode. The WebGL probe runs only when the other rules would pick GL. */
export function detect(quality, reduceMotion) {
  const mm = q => { try { return typeof matchMedia === 'function' && matchMedia(q).matches; } catch { return false; } };
  const facts = {
    quality, reduceMotion: !!reduceMotion,
    prefersReduced: mm('(prefers-reduced-motion: reduce)'),
    width: mm(`(max-width:${NARROW_PX}px)`) ? NARROW_PX : (typeof innerWidth === 'number' ? Math.max(innerWidth, NARROW_PX + 1) : 1280),
    cores: (typeof navigator !== 'undefined' && navigator.hardwareConcurrency) || 8,
    webgl: true,
  };
  if (heroMode(facts) === 'gl') facts.webgl = probeWebgl();
  return heroMode(facts);
}

function probeWebgl() {
  try {
    const c = document.createElement('canvas');
    const gl = c.getContext('webgl2') || c.getContext('webgl');
    if (!gl) return false;
    gl.getExtension('WEBGL_lose_context')?.loseContext();
    return true;
  } catch { return false; }
}
