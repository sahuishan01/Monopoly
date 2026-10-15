// Global keyboard shortcuts for the play screen. Blazor decides whether an action is legal;
// this module only maps keys to action names and filters out keys meant for form fields.
//   attach(dotnetRef) -> { dispose() }   calls dotnetRef.invokeMethodAsync('OnShortcut', action)

const KEYS = { ' ': 'roll', 'b': 'buy', 'e': 'end', 'm': 'mute', 'l': 'log', 'escape': 'escape' };

function typing(el) {
  if (!el || el.nodeType !== 1) return false;
  const tag = el.tagName;
  return tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA' || el.isContentEditable === true ||
    (typeof el.closest === 'function' && !!el.closest('[contenteditable=""],[contenteditable="true"]'));
}

function activates(el) {
  if (!el || el.nodeType !== 1) return false;
  const tag = el.tagName;
  return tag === 'BUTTON' || tag === 'A' || tag === 'SUMMARY' || (el.getAttribute && el.getAttribute('role') === 'button');
}

/** Pure mapping used by the listener (exported for tests). Returns an action name or null. */
export function classify(ev, active, modalOpen) {
  if (!ev || ev.ctrlKey || ev.metaKey || ev.altKey || ev.shiftKey || ev.isComposing) return null;
  const action = KEYS[(ev.key || '').toLowerCase()] || null;
  if (!action) return null;
  if (action === 'escape') return action;          // closing a dialog is always safe, even from a slider
  if (ev.repeat) return null;
  if (typing(active)) return null;
  // dialogs own the keyboard while open; B still buys from the purchase card and M still mutes
  if (modalOpen && action !== 'buy' && action !== 'mute') return null;
  if (action === 'roll' && activates(active)) return null; // let Space press the focused button
  return action;
}

export function attach(dotnet) {
  let ref = dotnet;
  const doc = globalThis.document;
  const onKey = ev => {
    if (!ref || ev.defaultPrevented) return;
    const modal = !!doc.querySelector('[aria-modal="true"]');
    const action = classify(ev, doc.activeElement, modal);
    if (!action) return;
    if (action !== 'escape' || modal) ev.preventDefault();
    ref.invokeMethodAsync('OnShortcut', action).catch(() => { });
  };
  doc.addEventListener('keydown', onKey);
  return {
    dispose() {
      doc.removeEventListener('keydown', onKey);
      ref = null;
    },
  };
}
