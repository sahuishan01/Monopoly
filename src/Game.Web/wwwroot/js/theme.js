export function apply(json) {
  const t = typeof json === "string" ? JSON.parse(json) : json;
  const root = document.documentElement;
  root.dataset.theme = t.id;
  for (const [k, v] of Object.entries(t.css || {})) root.style.setProperty(k, v);
  // Browser chrome (address bar, installed-app title bar) follows the theme background.
  const bg = (t.css && t.css['--bg']) || getComputedStyle(root).getPropertyValue('--bg').trim();
  if (bg) {
    let meta = document.querySelector('meta[name="theme-color"]');
    if (!meta) { meta = document.createElement('meta'); meta.name = 'theme-color'; document.head.appendChild(meta); }
    meta.content = bg;
  }
  return t.id;
}
