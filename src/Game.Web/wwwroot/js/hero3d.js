// Decorative Home hero: the 3D board (same scene, materials and fx modules as board3d.js) playing a scripted
// demo game behind the page. No session, no input, no Blazor callbacks.
//
// Unlike board3d.js (one board per page, module singleton) every start() returns its own instance, so a late
// dispose from a Home page that is navigating away can never touch the Play board's renderer.
// Frames are capped at 30 fps, and the loop stops while the tab is hidden or the canvas is scrolled out of view.
import * as THREE from './vendor/three.module.min.js';
import { createWorld } from './3d/scene.js';
import { createLook } from './3d/materials.js';
import { createFx } from './fx.js';

export const FPS = 30;
const live = { instances: 0, loops: 0, frames: 0, started: 0, disposed: 0 };

function parse(j) { try { return typeof j === 'string' ? JSON.parse(j) : j; } catch { return null; } }

/** Scripted turns: roll, hop, buy or build. Deterministic for a given seed so screenshots are stable. */
export function createDemo(board, seed = 7) {
  const n = board.Tiles.length;
  const ownable = board.Tiles.map(t => !!t.IsOwnable);
  let s = seed >>> 0;
  const rnd = () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
  const players = [0, 1, 2, 3].map(i => ({ Id: i, Position: [0, 6, 13, 24][i] % n, Bankrupt: false }));
  const fresh = () => board.Tiles.map((t, i) => (ownable[i] ? { Owner: -1, Level: 0, Mortgaged: false } : null));
  let props = fresh();
  // a few holdings so the board looks lived-in from the first frame
  [[1, 0, 2], [3, 0, 1], [6, 1, 0], [9, 1, 3], [11, 2, 1], [16, 3, 0], [21, 2, 4], [24, 3, 2], [29, 1, 0], [34, 0, 5], [39, 2, 2]]
    .forEach(([t, o, l]) => { if (props[t]) Object.assign(props[t], { Owner: o, Level: l }); });
  let turn = 0;
  const state = () => ({ Players: players.map(p => ({ ...p })), Properties: props.map(p => (p ? { ...p } : null)) });
  return {
    state,
    /** One turn as timed steps: [delaySeconds, events[], applyState?] */
    turn() {
      const p = players[turn % players.length];
      turn++;
      if (turn % 48 === 0) props = fresh();
      const d1 = 1 + Math.floor(rnd() * 6), d2 = 1 + Math.floor(rnd() * 6);
      const from = p.Position, to = (from + d1 + d2) % n;
      const steps = [[0, [{ $t: 'DiceRolled', Player: p.Id, D1: d1, D2: d2 }], false]];
      p.Position = to;
      steps.push([0.95, [{ $t: 'PlayerMoved', Player: p.Id, From: from, To: to }], true]);
      const pr = props[to], arrive = 0.95 + (d1 + d2) * 0.16 + 0.25;
      if (pr && pr.Owner < 0) { pr.Owner = p.Id; steps.push([arrive, [{ $t: 'PropertyPurchased', Player: p.Id, Tile: to }], true]); }
      else if (pr && pr.Owner === p.Id && pr.Level < 5) { pr.Level++; steps.push([arrive, [{ $t: 'BuildingConstructed', Player: p.Id, Tile: to }], true]); }
      else {
        // land elsewhere: grow a random holding of this player now and then so the skyline keeps changing
        const mine = props.map((x, i) => (x && x.Owner === p.Id && x.Level < 4 ? i : -1)).filter(i => i >= 0);
        if (mine.length && rnd() < 0.5) {
          const t = mine[Math.floor(rnd() * mine.length)];
          props[t].Level++;
          steps.push([arrive, [{ $t: 'BuildingConstructed', Player: p.Id, Tile: t }], true]);
        }
      }
      return steps;
    },
  };
}

/**
 * Starts the hero on `canvas`. opts: { quality: 'high'|'low' } ('high' turns on shadows and bloom).
 * Returns a handle { setTheme(json), dispose() }, or null when WebGL is unavailable.
 */
export function start(canvas, boardJson, themeJson, optsJson) {
  const opts = parse(optsJson) || {};
  const hi = opts.quality === 'high';
  let renderer = null, world = null, fx = null, raf = 0, io = null, ro = null;
  let running = false, visible = true, disposed = false, lastDraw = 0, clock = 0, nextTurn = 0.8, orbit = 0.6;
  const queue = [];
  try {
    const board = parse(boardJson);
    const theme = parse(themeJson);
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'low-power' });
    live.instances++;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    const look = createLook(theme);
    world = createWorld(board, look, renderer, { reduceMotion: false });
    world.setTheme(theme);
    world.setQuality(hi ? 'high' : 'low');
    const camera = new THREE.PerspectiveCamera(36, 1, 0.1, 300);
    fx = createFx(THREE, world.scene, renderer, camera, theme, hi ? 'high' : 'low');
    const demo = createDemo(board);
    world.applyState(demo.state());
    const side = world.side;
    live.started++;

    const resize = () => {
      if (!renderer) return;
      const w = Math.max(1, canvas.clientWidth), h = Math.max(1, canvas.clientHeight);
      renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, hi ? 1.5 : 1));
      renderer.setSize(w, h, false);
      camera.aspect = w / h;
      // Wide screens: the board sits low under the wordmark, left of the card column.
      if (w / h > 1.25) camera.setViewOffset(w, h, w * 0.13, -h * 0.24, w, h); else camera.clearViewOffset();
      camera.updateProjectionMatrix();
      fx.resize(w, h);
    };
    const place = () => {
      const r = side * (camera.aspect < 1 ? 2.3 : 3.0), phi = 0.98;
      camera.position.set(r * Math.sin(phi) * Math.sin(orbit), r * Math.cos(phi), r * Math.sin(phi) * Math.cos(orbit));
      camera.lookAt(0, -0.6, 0);
      const fog = world.scene.fog;
      if (fog) { fog.near = r + side * 0.8; fog.far = r + side * 5; }
    };
    const frame = now => {
      if (!running) return;
      raf = requestAnimationFrame(frame);
      if (lastDraw && now - lastDraw < 1000 / FPS - 2) return;
      const dt = lastDraw ? Math.min((now - lastDraw) / 1000, 0.1) : 1 / FPS;
      lastDraw = now;
      try {
        clock += dt; orbit += dt * 0.06;
        if (clock >= nextTurn) {
          for (const [at, events, apply] of demo.turn()) queue.push({ at: clock + at, events, apply });
          nextTurn = clock + 3.4;
        }
        for (let i = 0; i < queue.length;) {
          if (queue[i].at > clock) { i++; continue; }
          const q = queue.splice(i, 1)[0];
          for (const e of q.events) {
            world.playEvent(e, false);
            if (e.$t === 'PropertyPurchased' || e.$t === 'BuildingConstructed') { const t = world.tiles[e.Tile]; if (t) fx.burst(t.mesh.position, 'build'); }
          }
          if (q.apply) world.applyState(demo.state());
        }
        place();
        world.step(dt);
        fx.render(dt);
        live.frames++;
        if (!canvas.classList.contains('ready')) canvas.classList.add('ready');
      } catch (e) { if (!frame.warned) { frame.warned = true; console.warn('hero frame failed', e); } }
    };
    const run = () => {
      const want = !disposed && visible && !document.hidden;
      if (want && !running) { running = true; lastDraw = 0; live.loops++; raf = requestAnimationFrame(frame); }
      else if (!want && running) { running = false; cancelAnimationFrame(raf); raf = 0; live.loops--; }
    };
    const onVis = () => run();
    const onLost = e => { e.preventDefault(); visible = false; run(); };
    document.addEventListener('visibilitychange', onVis);
    canvas.addEventListener('webglcontextlost', onLost);
    if (typeof IntersectionObserver !== 'undefined') {
      io = new IntersectionObserver(entries => { visible = entries[entries.length - 1].isIntersecting; run(); }, { threshold: 0.02 });
      io.observe(canvas);
    }
    if (typeof ResizeObserver !== 'undefined') { ro = new ResizeObserver(resize); ro.observe(canvas); }
    window.addEventListener('resize', resize);
    resize(); place();
    run();

    return {
      setTheme(json) {
        const t = parse(json);
        if (!t || disposed) return;
        world.setTheme(t);
        fx.setTheme(t);
      },
      /** For tests: whether the render loop is currently scheduled. */
      running: () => running,
      dispose() {
        if (disposed) return;
        disposed = true;
        run();
        document.removeEventListener('visibilitychange', onVis);
        canvas.removeEventListener('webglcontextlost', onLost);
        window.removeEventListener('resize', resize);
        if (io) io.disconnect();
        if (ro) ro.disconnect();
        teardown();
      },
    };
  } catch (e) {
    console.warn('hero init failed', e);
    disposed = true;
    if (running) { running = false; cancelAnimationFrame(raf); live.loops--; }
    teardown();
    return null;
  }

  function teardown() {
    try { if (fx) fx.dispose(); } catch { /* ignore */ }
    try { if (world) world.dispose(); } catch { /* ignore */ }
    if (renderer) {
      renderer.dispose();
      // Release the GL context now instead of waiting for GC: Home -> Play -> Home must not pile up contexts.
      try { renderer.forceContextLoss(); } catch { /* ignore */ }
      renderer = null;
      live.instances--; live.disposed++;
    }
    fx = null; world = null;
  }
}

/** Test-only counters (home-check.mjs). Not part of the app API. */
export const __test = { stats: () => ({ ...live }) };
