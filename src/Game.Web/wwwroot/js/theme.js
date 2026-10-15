export function apply(json) {
  const t = typeof json === "string" ? JSON.parse(json) : json;
  const root = document.documentElement;
  // Switch with transitions off: Chrome can leave a colour transition between two var()/color-mix() values stuck
  // on the old theme's colour. The class is dropped two frames later, after the new colours are computed.
  root.classList.add('theme-switching');
  root.dataset.theme = t.id;
  for (const [k, v] of Object.entries(t.css || {})) root.style.setProperty(k, v);
  void getComputedStyle(root).color;
  const gen = (apply.gen = (apply.gen || 0) + 1);
  const done = () => { if (apply.gen === gen) root.classList.remove('theme-switching'); };
  if (typeof requestAnimationFrame === 'function') requestAnimationFrame(() => requestAnimationFrame(done)); else setTimeout(done, 50);
  // Browser chrome (address bar, installed-app title bar) follows the theme background.
  const bg = (t.css && t.css['--bg']) || getComputedStyle(root).getPropertyValue('--bg').trim();
  if (bg) {
    let meta = document.querySelector('meta[name="theme-color"]');
    if (!meta) { meta = document.createElement('meta'); meta.name = 'theme-color'; document.head.appendChild(meta); }
    meta.content = bg;
  }
  return t.id;
}
