// Pure motion rules for the 3D board (no THREE, no DOM beyond an injectable matchMedia) so node tests can cover them.
export const REDUCE_QUERY = '(prefers-reduced-motion: reduce)';

/** The OS "reduce motion" preference; false where matchMedia is missing or throws. */
export function osReducedMotion(mm = globalThis.matchMedia) {
  try { return typeof mm === 'function' && !!mm.call(globalThis, REDUCE_QUERY).matches; } catch { return false; }
}

/** Motion is reduced when either the in-app setting or the OS preference asks for it. */
export function effectiveReduce(app, os) { return !!app || !!os; }

/** Particles and other one-shot effects play only for live events: not in a burst and not under reduced motion. */
export function playsFx(burst, reduce) { return !burst && !reduce; }

/**
 * Camera move an event asks for: 'follow' (TurnStarted), 'overview' (auctions, trades) or null.
 * Under reduced motion there is none: the camera stays put instead of gliding (and is never teleported).
 */
export function cameraCue(type, reduce) {
  if (reduce || typeof type !== 'string') return null;
  if (type === 'TurnStarted') return 'follow';
  if (type === 'AuctionStarted' || type.startsWith('Trade')) return 'overview';
  return null;
}
