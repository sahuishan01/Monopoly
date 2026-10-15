export function apply(json) {
  const t = typeof json === "string" ? JSON.parse(json) : json;
  const root = document.documentElement;
  root.dataset.theme = t.id;
  for (const [k, v] of Object.entries(t.css || {})) root.style.setProperty(k, v);
  return t.id;
}
