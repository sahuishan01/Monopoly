// Small DOM helpers for dialogs and sheets: focus trap with focus restore, swipe-down to close, capability probes.

const FOCUSABLE = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])';

function focusables(root) {
  return [...root.querySelectorAll(FOCUSABLE)].filter(e => e.offsetParent !== null || e === document.activeElement);
}

/** Traps Tab inside `root`, focuses its [data-autofocus] or first control, and restores the previous focus on release(). */
export function trap(root) {
  const before = document.activeElement;
  const onKey = ev => {
    if (ev.key !== 'Tab') return;
    const list = focusables(root);
    if (list.length === 0) { ev.preventDefault(); root.focus(); return; }
    const first = list[0], last = list[list.length - 1];
    if (ev.shiftKey && (document.activeElement === first || !root.contains(document.activeElement))) { ev.preventDefault(); last.focus(); }
    else if (!ev.shiftKey && (document.activeElement === last || !root.contains(document.activeElement))) { ev.preventDefault(); first.focus(); }
  };
  root.addEventListener('keydown', onKey);
  const start = root.querySelector('[data-autofocus]') || focusables(root)[0] || root;
  try { start.focus({ preventScroll: true }); } catch { }
  return {
    release() {
      root.removeEventListener('keydown', onKey);
      if (before && before.isConnected && typeof before.focus === 'function') {
        try { before.focus({ preventScroll: true }); } catch { }
      }
    },
  };
}

/** Drag a bottom sheet down to dismiss it. Only acts while the sheet is scrolled to the top. */
export function swipe(panel, dotnet, method) {
  let y0 = null, dy = 0, ref = dotnet;
  const sheetMode = () => getComputedStyle(panel).getPropertyValue('--sheet').trim() === '1';
  const start = ev => {
    if (!sheetMode() || ev.touches.length !== 1 || panel.scrollTop > 0) return;
    y0 = ev.touches[0].clientY; dy = 0;
  };
  const move = ev => {
    if (y0 === null) return;
    dy = Math.max(0, ev.touches[0].clientY - y0);
    if (dy > 0) {
      ev.preventDefault();
      panel.style.transition = 'none';
      panel.style.transform = `translateY(${dy}px)`;
    }
  };
  const end = () => {
    if (y0 === null) return;
    y0 = null;
    panel.style.transition = '';
    panel.style.transform = '';
    if (dy > 80 && ref) ref.invokeMethodAsync(method).catch(() => { });
    dy = 0;
  };
  panel.addEventListener('touchstart', start, { passive: true });
  panel.addEventListener('touchmove', move, { passive: false });
  panel.addEventListener('touchend', end);
  panel.addEventListener('touchcancel', end);
  return {
    dispose() {
      ref = null;
      panel.removeEventListener('touchstart', start);
      panel.removeEventListener('touchmove', move);
      panel.removeEventListener('touchend', end);
      panel.removeEventListener('touchcancel', end);
    },
  };
}

/**
 * Mobile HUD bottom sheet. Reports the layout mode (CSS sets `--hud-sheet: 1` on the play page in
 * bottom-sheet mode) to `dotnet.HudMode(bool)` whenever it changes, and turns a vertical drag on the
 * sheet's handle area into `dotnet.SetHud(open)`. A plain tap is left to the handle's own click.
 */
export function hudSheet(sheet, dotnet) {
  let ref = dotnet, last = null, drag = null, swallow = false;
  const isSheet = () => getComputedStyle(sheet).getPropertyValue('--hud-sheet').trim() === '1';
  const isOpen = () => !!sheet.closest('.hud-open');
  const report = () => {
    const m = isSheet();
    if (m !== last) { last = m; ref?.invokeMethodAsync('HudMode', m).catch(() => { }); }
  };
  const handle = ev => ev.target instanceof Element && ev.target.closest('.peek-toggle') && sheet.contains(ev.target);
  const down = ev => {
    if (!isSheet() || !handle(ev) || (ev.pointerType === 'mouse' && ev.button !== 0)) return;
    drag = { id: ev.pointerId, y0: ev.clientY, dy: 0, open: isOpen(), moving: false };
  };
  const move = ev => {
    if (!drag || ev.pointerId !== drag.id) return;
    const dy = ev.clientY - drag.y0;
    if (!drag.moving && Math.abs(dy) < 8) return;
    if (!drag.moving) { drag.moving = true; try { sheet.setPointerCapture(ev.pointerId); } catch { } sheet.classList.add('dragging'); }
    const peek = parseFloat(getComputedStyle(sheet).getPropertyValue('--peek-px')) || 72;
    const travel = Math.max(0, sheet.offsetHeight - peek);
    drag.dy = drag.open ? Math.min(travel, Math.max(0, dy)) : Math.max(-travel, Math.min(0, dy));
    sheet.style.setProperty('--drag', drag.dy + 'px');
    ev.preventDefault();
  };
  const up = ev => {
    if (!drag || ev.pointerId !== drag.id) return;
    const d = drag; drag = null;
    if (!d.moving) return;
    swallow = true; setTimeout(() => { swallow = false; }, 400);   // the drag must not also toggle via click
    sheet.classList.remove('dragging');
    sheet.style.removeProperty('--drag');
    const open = d.open ? d.dy < 60 : d.dy < -60;
    if (open !== d.open) ref?.invokeMethodAsync('SetHud', open).catch(() => { });
  };
  const click = ev => { if (swallow) { swallow = false; ev.stopPropagation(); ev.preventDefault(); } };
  sheet.addEventListener('pointerdown', down);
  sheet.addEventListener('pointermove', move);
  sheet.addEventListener('pointerup', up);
  sheet.addEventListener('pointercancel', up);
  sheet.addEventListener('click', click, true);
  addEventListener('resize', report);
  addEventListener('orientationchange', report);
  report();
  return {
    refresh: report,
    dispose() {
      ref = null;
      sheet.removeEventListener('pointerdown', down);
      sheet.removeEventListener('pointermove', move);
      sheet.removeEventListener('pointerup', up);
      sheet.removeEventListener('pointercancel', up);
      sheet.removeEventListener('click', click, true);
      removeEventListener('resize', report);
      removeEventListener('orientationchange', report);
    },
  };
}

/** True when a WebGL context can be created (cheap probe, context released at once). */
export function webgl() {
  try {
    const c = document.createElement('canvas');
    const gl = c.getContext('webgl2') || c.getContext('webgl');
    if (!gl) return false;
    gl.getExtension('WEBGL_lose_context')?.loseContext();
    return true;
  } catch { return false; }
}

export function reducedMotion() {
  try { return matchMedia('(prefers-reduced-motion: reduce)').matches; } catch { return false; }
}
