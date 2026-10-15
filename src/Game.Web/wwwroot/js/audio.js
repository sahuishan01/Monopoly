// Synthesized sound engine: pure WebAudio, no files. Every entry point is a no-op without AudioContext.
const MAX_VOICES = 12;
const PENT = [0, 2, 4, 7, 9, 12, 14, 16];
let theme = { wave: 'triangle', root: 196 };
let vol = { master: 1, sfx: 1, music: 0.5, muted: false };
let ctx = null, master = null, sfxBus = null, musicBus = null, comp = null, noiseBuf = null;
let voices = [];
let ambient = null;
let listening = false, disposed = false, ambientWanted = false;
const GESTURES = ['pointerdown', 'keydown', 'click', 'touchend'];

function Ctor() { return globalThis.AudioContext || globalThis.webkitAudioContext || null; }
function parse(j) { try { return typeof j === 'string' ? JSON.parse(j) : (j || {}); } catch { return {}; } }
function clamp01(v) { v = Number(v); return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0; }
function hz(semi) { return theme.root * Math.pow(2, semi / 12); }

function applyTheme(t) {
  const s = (t && t.sound) || t || {};
  if (typeof s.root === 'number' && s.root > 20 && s.root < 4000) theme.root = s.root;
  if (['sine', 'square', 'sawtooth', 'triangle'].includes(s.wave)) theme.wave = s.wave;
}
function applyGains() {
  if (!ctx) return;
  const m = vol.muted ? 0 : vol.master;
  try {
    const t = ctx.currentTime;
    master.gain.setTargetAtTime(m, t, 0.02);
    sfxBus.gain.setTargetAtTime(vol.sfx, t, 0.02);
    musicBus.gain.setTargetAtTime(vol.music * 0.5, t, 0.02);
  } catch { }
}
function onGesture() {
  if (ctx && ctx.state === 'suspended') { try { const p = ctx.resume(); if (p && p.catch) p.catch(() => { }); } catch { } }
}
function listen() {
  if (listening || !globalThis.addEventListener) return;
  listening = true;
  for (const ev of GESTURES) globalThis.addEventListener(ev, onGesture, true);
}
function unlisten() {
  if (!listening) return;
  listening = false;
  try { for (const ev of GESTURES) globalThis.removeEventListener(ev, onGesture, true); } catch { }
}
function ensure() {
  if (disposed) return false;
  if (ctx) return true;
  const C = Ctor();
  if (!C) return false;
  try {
    ctx = new C();
    comp = ctx.createDynamicsCompressor();
    comp.threshold.value = -14; comp.ratio.value = 12; comp.attack.value = 0.003; comp.release.value = 0.2;
    master = ctx.createGain(); sfxBus = ctx.createGain(); musicBus = ctx.createGain();
    sfxBus.connect(master); musicBus.connect(master); master.connect(comp); comp.connect(ctx.destination);
    const n = Math.floor(ctx.sampleRate * 0.5);
    noiseBuf = ctx.createBuffer(1, n, ctx.sampleRate);
    const d = noiseBuf.getChannelData(0);
    for (let i = 0; i < n; i++) d[i] = Math.random() * 2 - 1;
    applyGains();
    onGesture();
    return true;
  } catch { ctx = null; return false; }
}

function track(nodes, stopAt) {
  const v = { nodes, dead: false };
  voices.push(v);
  const end = () => {
    if (v.dead) return;
    v.dead = true;
    voices = voices.filter(x => x !== v);
    for (const n of nodes) { try { n.disconnect(); } catch { } }
  };
  v.kill = () => {
    for (const n of nodes) { try { if (n.stop) n.stop(); } catch { } }
    end();
  };
  // End the voice from the source that stops LAST, never the first.
  let last = null;
  for (const n of nodes) if (n.__last && (!last || n.__end >= last.__end)) last = n;
  if (last) last.onended = end;
  else if (typeof setTimeout === 'function') setTimeout(end, Math.max(0, (stopAt - ctx.currentTime) * 1000 + 100));
  while (voices.length > MAX_VOICES) voices[0].kill();
  return v;
}

// One enveloped oscillator note. Returns nodes for tracking.
function tone(type, freq, t0, dur, peak, dest, glideTo) {
  const o = ctx.createOscillator(), g = ctx.createGain();
  o.type = type; o.frequency.setValueAtTime(freq, t0);
  if (glideTo) o.frequency.exponentialRampToValueAtTime(Math.max(20, glideTo), t0 + dur);
  g.gain.setValueAtTime(0.0001, t0);
  g.gain.linearRampToValueAtTime(peak, t0 + 0.008);
  g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
  o.connect(g); g.connect(dest);
  o.start(t0); o.stop(t0 + dur + 0.02);
  o.__last = true; o.__end = t0 + dur + 0.02;
  return [o, g];
}
function noise(t0, dur, freq, q, peak, dest, type) {
  const s = ctx.createBufferSource(), f = ctx.createBiquadFilter(), g = ctx.createGain();
  s.buffer = noiseBuf; f.type = type || 'bandpass'; f.frequency.value = freq; f.Q.value = q;
  g.gain.setValueAtTime(peak, t0);
  g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
  s.connect(f); f.connect(g); g.connect(dest);
  s.start(t0); s.stop(t0 + dur + 0.02);
  s.__last = true; s.__end = t0 + dur + 0.02;
  return [s, f, g];
}

const VOICES = {
  dice(t, w, d) {
    let nodes = [], at = t, end = t;
    for (let i = 0; i < 7; i++) {
      nodes = nodes.concat(noise(at, 0.04, 1800 + Math.random() * 2500, 6, 0.5, d));
      end = at + 0.05;
      at += 0.045 + i * 0.012;
    }
    return [nodes, end];
  },
  hop(t, w, d, k) {
    const semi = PENT[Math.min(PENT.length - 1, Math.floor(((k || 0) % 40) / 5))] + 12;
    return [tone(w, hz(semi), t, 0.09, 0.3, d), t + 0.12];
  },
  cash(t, w, d) {
    return [[...tone(w, hz(19), t, 0.1, 0.3, d), ...tone(w, hz(24), t + 0.08, 0.2, 0.3, d)], t + 0.32];
  },
  buy(t, w, d) { return [[...tone('sine', hz(-12), t, 0.18, 0.6, d, hz(-17)), ...tone(w, hz(7), t + 0.05, 0.2, 0.25, d)], t + 0.3]; },
  sell(t, w, d) { return [[...tone('sine', hz(-12), t, 0.18, 0.6, d, hz(-19)), ...tone(w, hz(0), t + 0.05, 0.2, 0.25, d, hz(-5))], t + 0.3]; },
  build(t, w, d) { return [[...noise(t, 0.08, 400, 1, 0.6, d, 'lowpass'), ...tone('sine', hz(-5), t, 0.2, 0.5, d, hz(-10)), ...tone(w, hz(12), t + 0.1, 0.15, 0.2, d)], t + 0.35]; },
  jail(t, w, d) { return [[...tone('sawtooth', hz(-12), t, 0.5, 0.25, d, hz(-24)), ...tone('sine', hz(-17), t + 0.05, 0.5, 0.4, d, hz(-29))], t + 0.6]; },
  card(t, w, d) { return [noise(t, 0.18, 3000, 1.5, 0.35, d, 'highpass'), t + 0.22]; },
  hammer(t, w, d) { return [[...noise(t, 0.05, 1500, 2, 0.6, d), ...tone('sine', hz(-24), t, 0.25, 0.7, d, hz(-30))], t + 0.3]; },
  trade(t, w, d) { return [[...tone(w, hz(12), t, 0.12, 0.3, d), ...tone(w, hz(19), t + 0.12, 0.2, 0.3, d)], t + 0.35]; },
  bankrupt(t, w, d) {
    let n = [];
    [12, 8, 3, -4].forEach((s, i) => { n = n.concat(tone('sawtooth', hz(s), t + i * 0.18, 0.3, 0.25, d)); });
    return [n, t + 1.0];
  },
  win(t, w, d) {
    let n = [];
    [0, 4, 7, 12, 16, 19, 24].forEach((s, i) => { n = n.concat(tone(w, hz(s + 12), t + i * 0.11, 0.35, 0.3, d)); });
    return [n, t + 1.2];
  },
  chime(t, w, d) {
    let n = [];
    [[1, 0.4], [2.76, 0.22], [5.4, 0.12]].forEach(([r, a]) => { n = n.concat(tone('sine', hz(12) * r, t, 0.8 / r + 0.2, a, d)); });
    return [n, t + 1.0];
  },
};

export function init(themeJson, settingsJson) {
  disposed = false; ambientWanted = false;
  applyTheme(parse(themeJson));
  const s = parse(settingsJson);
  setVolumes(s.master ?? s.Master ?? vol.master, s.sfx ?? s.Sfx ?? vol.sfx, s.music ?? s.Music ?? vol.music, s.muted ?? s.Muted ?? vol.muted);
  if (Ctor()) listen();
}
export function setTheme(themeJson) { applyTheme(parse(themeJson)); }
export function setVolumes(m, s, mu, muted) {
  vol = { master: clamp01(m), sfx: clamp01(s), music: clamp01(mu), muted: !!muted };
  applyGains();
  syncAmbient();
}
export function play(id, intensity = 1) {
  const fn = VOICES[id];
  if (!fn || vol.muted || vol.master <= 0 || vol.sfx <= 0) return;
  if (!ensure()) return;
  try {
    const t = ctx.currentTime + 0.01;
    const [nodes, end] = fn(t, theme.wave, sfxBus, Number(intensity) || 0);
    track(nodes, end);
  } catch { }
}
export function startAmbient() { ambientWanted = true; syncAmbient(); }
function syncAmbient() {
  const should = ambientWanted && !disposed && !vol.muted && vol.music > 0 && vol.master > 0;
  if (!should) { haltAmbient(); return; }
  if (ambient || !ensure()) return;
  try {
    const lp = ctx.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 700;
    const g = ctx.createGain(); g.gain.value = 0.25;
    const o1 = ctx.createOscillator(), o2 = ctx.createOscillator();
    o1.type = 'sine'; o2.type = theme.wave; o2.detune.value = 7;
    o1.connect(lp); o2.connect(lp); lp.connect(g); g.connect(musicBus);
    let idx = 2;
    const step = () => {
      idx = Math.min(PENT.length - 1, Math.max(0, idx + (Math.random() < 0.5 ? -1 : 1) * (1 + (Math.random() < 0.3 ? 1 : 0))));
      const f = hz(PENT[idx] - 12), t = ctx.currentTime;
      o1.frequency.setTargetAtTime(f, t, 0.8); o2.frequency.setTargetAtTime(f, t, 0.8);
    };
    o1.frequency.value = o2.frequency.value = hz(PENT[idx] - 12);
    o1.start(); o2.start();
    const timer = setInterval(step, 2000 + Math.random() * 2000);
    ambient = { nodes: [o1, o2, lp, g], timer, o: [o1, o2] };
  } catch { ambient = null; }
}
export function stopAmbient() { ambientWanted = false; haltAmbient(); }
function haltAmbient() {
  if (!ambient) return;
  clearInterval(ambient.timer);
  for (const o of ambient.o) { try { o.stop(); } catch { } }
  for (const n of ambient.nodes) { try { n.disconnect(); } catch { } }
  ambient = null;
}
export function dispose() {
  disposed = true; ambientWanted = false;
  unlisten();
  stopAmbient();
  for (const v of [...voices]) v.kill();
  voices = [];
  if (ctx) { try { const p = ctx.close(); if (p && p.catch) p.catch(() => { }); } catch { } }
  ctx = master = sfxBus = musicBus = comp = noiseBuf = null;
}
export function _stats() { return { voices: voices.length, ambient: !!ambient, root: theme.root, wave: theme.wave }; }
