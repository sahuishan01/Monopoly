# BoardEmpire Web "Studio" Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the Blazor + three.js web client into a themed, animated, sound-enabled, mobile-first game UI with 2D and 3D parity.

**Architecture:** Presentation-only. A pure C# cue layer (`Cues`) maps released `GameEvent`s to animation + sound cues; Blazor services own theme/settings; JS modules (`3d/*`, `audio.js`, `fx.js`) render and play cues. Game rules, protocol and server are untouched.

**Tech Stack:** .NET 8 Blazor WASM, three.js (vendored, pinned), WebAudio, SVG/CSS, xUnit, Playwright (existing `tools/web-smoke`).

**Spec:** `docs/superpowers/specs/2026-10-15-web-studio-redesign-design.md`

Work in `/home/opc/projects/monopoly-web` (branch `feat/web-client`). All paths below are relative to it.

## Global Constraints

- No changes to `src/Game.Core`, `src/Game.Net`, `src/Game.Protocol`, `src/Game.Server`.
- Views only animate; commands go through the existing session layer.
- three.js stays version-pinned and vendored under `src/Game.Web/wwwroot/js/vendor/`; any added examples files must match that exact version and ship with the licence.
- No audio asset files (synthesized only). Procedural textures only (no image/glTF assets).
- WebGL init failure falls back to 2D (existing `OnUnavailable`).
- First load <= 25 MB. Post-processing modules lazy-loaded.
- Touch targets >= 44 px on mobile; honour `prefers-reduced-motion`.
- `dotnet test BoardEmpire.sln` stays green after every task.
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Review Focus

- Theme JSON missing a key or unknown theme id in localStorage -> falls back to Neon Noir, no crash.
- localStorage unavailable/throws (private window) -> settings live in memory, UI still works.
- AudioContext blocked until gesture, or WebAudio absent -> silent, no exceptions, mute toggle still works.
- Event backlog burst (resync, 60+ events) -> cues collapse (no 60 overlapping sounds/particles) and state ends correct.
- Resize/orientation change and `dispose()` mid-animation -> no leaked rAF loops, contexts or listeners; re-init works.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `src/Game.WebClient/Cues.cs` | Pure map `GameEvent -> Cue` (animation kind, sound id, intensity), burst collapsing |
| `src/Game.WebClient/EventPacer.cs` | Modify: use cue durations |
| `src/Game.WebClient/ThemeCatalog.cs` | Theme ids, fallback, JSON validation |
| `src/Game.Web/Services/SettingsService.cs` | Quality/audio/motion/theme/view; localStorage w/ in-memory fallback |
| `src/Game.Web/wwwroot/themes/*.json` | 4 theme data files |
| `src/Game.Web/wwwroot/css/{tokens,layout,components,board2d}.css` | Split of `app.css` |
| `src/Game.Web/wwwroot/js/3d/{scene,materials,camera,motion}.js` | 3D modules |
| `src/Game.Web/wwwroot/js/fx.js` | Particles, post-processing, quality probe |
| `src/Game.Web/wwwroot/js/audio.js` | WebAudio engine |
| `src/Game.Web/wwwroot/js/board3d.js` | Thin facade keeping existing API |
| `src/Game.Web/Components/{Board2D,Board3D,Hud,PropertyCard,Toasts,EventLog,SettingsSheet,DiceTray}.razor` | UI |
| `src/Game.Web/wwwroot/{manifest.webmanifest,icons/*}` | PWA |
| `tests/Web.Tests/{CuesTests,ThemeCatalogTests}.cs` | Unit tests |
| `tools/web-smoke/*` | Extend Playwright |

---

### Task 1: Cue layer

**Files:**
- Create: `src/Game.WebClient/Cues.cs`
- Modify: `src/Game.WebClient/EventPacer.cs`
- Test: `tests/Web.Tests/CuesTests.cs`

**Interfaces:**
- Produces: `public enum CueKind { None, Dice, Move, Buy, Build, Rent, Cash, Jail, Card, Auction, Trade, Bankrupt, Win, Turn }`; `public readonly record struct Cue(CueKind Kind, string Sound, double Seconds)`; `public static class Cues { public static Cue For(GameEvent e); public static IReadOnlyList<Cue> Collapse(IEnumerable<GameEvent> batch, int maxPerKind = 3); }`

- [ ] **Step 1: Write failing tests**

```csharp
using Game.Core.Events;
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class CuesTests
{
    [Fact]
    public void Dice_and_move_map_to_their_kinds()
    {
        Assert.Equal(CueKind.Dice, Cues.For(new DiceRolled(0, 3, 4, 0)).Kind);
        Assert.Equal(CueKind.Move, Cues.For(new PlayerMoved(0, 0, 7, false)).Kind);
    }

    [Fact]
    public void Unknown_events_have_no_cue_and_no_sound()
    {
        var c = Cues.For(new TurnEnded(0));
        Assert.Equal("", c.Sound.Length == 0 ? "" : c.Sound);
    }

    [Fact]
    public void Collapse_caps_each_kind_in_a_burst()
    {
        var burst = Enumerable.Range(0, 60).Select(_ => (GameEvent)new DiceRolled(0, 1, 2, 0));
        Assert.True(Cues.Collapse(burst, 3).Count <= 3);
    }
}
```

Note: confirm exact constructor signatures of `PlayerMoved`/`TurnEnded` in `src/Game.Core/Events/GameEvents.cs` before running and adjust arguments only.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/Web.Tests --filter CuesTests`
Expected: FAIL (`Cues` not defined)

- [ ] **Step 3: Implement**

```csharp
using Game.Core.Events;

namespace Game.WebClient;

public enum CueKind { None, Dice, Move, Buy, Build, Rent, Cash, Jail, Card, Auction, Trade, Bankrupt, Win, Turn }

public readonly record struct Cue(CueKind Kind, string Sound, double Seconds);

public static class Cues
{
    public static Cue For(GameEvent e) => e switch
    {
        DiceRolled => new(CueKind.Dice, "dice", 0.9),
        PlayerMoved => new(CueKind.Move, "hop", 0.7),
        PropertyPurchased => new(CueKind.Buy, "buy", 0.5),
        BuildingConstructed => new(CueKind.Build, "build", 0.6),
        BuildingSold => new(CueKind.Build, "sell", 0.4),
        RentPaid or MoneyTransferred or SalaryPaid => new(CueKind.Cash, "cash", 0.5),
        PlayerSentToJail => new(CueKind.Jail, "jail", 0.8),
        CardDrawn => new(CueKind.Card, "card", 0.6),
        AuctionStarted or AuctionCompleted => new(CueKind.Auction, "hammer", 0.6),
        TradeAccepted => new(CueKind.Trade, "trade", 0.5),
        PlayerBankrupt => new(CueKind.Bankrupt, "bankrupt", 0.9),
        GameEnded => new(CueKind.Win, "win", 1.2),
        TurnStarted => new(CueKind.Turn, "chime", 0.3),
        _ => new(CueKind.None, "", 0.2),
    };

    /// <summary>For large bursts (resync) keeps at most <paramref name="maxPerKind"/> cues of each kind.</summary>
    public static IReadOnlyList<Cue> Collapse(IEnumerable<GameEvent> batch, int maxPerKind = 3)
    {
        var seen = new Dictionary<CueKind, int>();
        var result = new List<Cue>();
        foreach (var e in batch)
        {
            var c = For(e);
            if (c.Kind == CueKind.None) continue;
            seen[c.Kind] = seen.GetValueOrDefault(c.Kind) + 1;
            if (seen[c.Kind] <= maxPerKind) result.Add(c);
        }
        return result;
    }
}
```

In `EventPacer.DelayFor`, replace the `basis` switch with `double basis = Cues.For(e).Seconds;` keeping the backlog scaling.

- [ ] **Step 4: Run all web tests**

Run: `dotnet test tests/Web.Tests`
Expected: PASS (existing `EventPacerTests` still pass; `DelayFor` DiceRolled = 0.9, PlayerMoved = 0.7 unchanged)

- [ ] **Step 5: Commit**

```bash
git add src/Game.WebClient tests/Web.Tests
git commit -m "feat(web): cue layer mapping events to animation and sound"
```

---

### Task 2: Themes, settings and CSS tokens

**Files:**
- Create: `src/Game.Web/wwwroot/themes/{neon-noir,classic-gold,frost,sakura}.json`, `src/Game.WebClient/ThemeCatalog.cs`, `src/Game.Web/Services/SettingsService.cs`, `src/Game.Web/wwwroot/css/{tokens,layout,components,board2d}.css`
- Modify: `src/Game.Web/wwwroot/index.html`, `src/Game.Web/Program.cs`, delete `css/app.css` after split
- Test: `tests/Web.Tests/ThemeCatalogTests.cs`

**Interfaces:**
- Produces: `ThemeCatalog.Ids : IReadOnlyList<string>`, `ThemeCatalog.Default = "neon-noir"`, `ThemeCatalog.Resolve(string? id) : string`, `ThemeCatalog.Validate(string json) : IReadOnlyList<string>` (list of missing keys; empty = valid). `SettingsService` with `Theme`, `Quality` ("auto"|"high"|"low"), `Master`, `Sfx`, `Music` (0..1), `Muted`, `ReduceMotion`, `View3d`, event `Changed`, `Task LoadAsync()`, `Task SaveAsync()`; registered scoped in `Program.cs`.
- Theme JSON required keys: `id`, `name`, `css` (object of `--*` tokens incl. `--bg --panel --text --muted --accent --bad --line --glow`), `scene` (`sky:[top,bottom]`, `felt`, `fog`, `ambient`, `sun`, `bloom`), `token` (`metal`, `rough`), `sound` (`wave`, `root`).

- [ ] **Step 1: Write failing tests**

```csharp
using Game.WebClient;
using Xunit;

namespace Web.Tests;

public class ThemeCatalogTests
{
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("nope")]
    public void Unknown_theme_resolves_to_default(string? id) =>
        Assert.Equal(ThemeCatalog.Default, ThemeCatalog.Resolve(id));

    [Fact]
    public void Known_theme_resolves_to_itself() => Assert.Equal("frost", ThemeCatalog.Resolve("frost"));

    [Fact]
    public void Validate_lists_missing_keys() =>
        Assert.Contains("scene", ThemeCatalog.Validate("{\"id\":\"x\",\"name\":\"X\",\"css\":{}}"));

    [Fact]
    public void Shipped_themes_are_valid()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "../../../../../src/Game.Web/wwwroot/themes");
        foreach (var id in ThemeCatalog.Ids)
            Assert.Empty(ThemeCatalog.Validate(File.ReadAllText(Path.Combine(dir, id + ".json"))));
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test tests/Web.Tests --filter ThemeCatalogTests` -> FAIL.

- [ ] **Step 3: Implement**
  1. `ThemeCatalog.cs`: `Ids = ["neon-noir","classic-gold","frost","sakura"]`; `Resolve` returns id if in `Ids` else `Default`; `Validate` parses with `System.Text.Json`, returns every required top-level/nested key missing (invalid JSON -> `["json"]`).
  2. Write the four theme JSON files with distinct palettes (Neon Noir: `--bg #07080f --accent #22f5c4 --glow #7c3aed`, sky `#0b1030/#02030a`, bloom 0.9; Classic Gold: `--bg #14100a --accent #e6b04a`, bloom 0.35; Frost: `--bg #0a141c --accent #7dd3fc`, bloom 0.5; Sakura: `--bg #1a0f16 --accent #f9a8d4`, bloom 0.6). Each includes all required keys and a `sound` block (`wave` one of sine/triangle/square, `root` Hz).
  3. Split `app.css`: variables -> `tokens.css` (declared on `:root` and overridden at runtime from theme JSON), remaining rules by area; add the four `<link>`s to `index.html`; delete `app.css`.
  4. `SettingsService`: reads/writes via `IJSRuntime` `localStorage` inside try/catch; on any failure keeps in-memory values; `Theme` setter calls `ThemeCatalog.Resolve`.
  5. Add `wwwroot/js/theme.js` exporting `apply(json)` that sets `document.documentElement.dataset.theme` and each `css` token via `style.setProperty`; `SettingsService.SetThemeAsync` fetches `themes/{id}.json`, calls `apply`, falls back to default on fetch failure.
  6. `Program.cs`: `builder.Services.AddScoped<SettingsService>();`

- [ ] **Step 4: Run** `dotnet test tests/Web.Tests` -> PASS; `dotnet build src/Game.Web` -> succeeds.

- [ ] **Step 5: Commit** `feat(web): theme catalog, settings service and split css tokens`

---

### Task 3: 3D foundation (modules, PBR, shadows, sky, themes)

**Files:**
- Create: `src/Game.Web/wwwroot/js/3d/{materials,scene,camera}.js`
- Modify: `src/Game.Web/wwwroot/js/board3d.js` (becomes facade)

**Interfaces:**
- Consumes: theme JSON `scene`/`token` blocks (Task 2).
- Produces (facade, `board3d.js` exports): `init(canvas, boardJson, dotnetRef, optsJson)`, `setTheme(themeJson)`, `setQuality("high"|"low")`, `applyState(stateJson)`, `playEvent(eventJson)`, `dispose()`. `optsJson = {theme, quality, reduceMotion}`. Existing callers keep working (`optsJson` optional).
- `materials.js`: `makeMaterials(theme) -> {tile(color), token(hue), building(hue), felt, sky}` and `makeEnvMap(renderer, theme)`; `scene.js`: `buildBoard(THREE, board, theme) -> {group, tiles[]}`; `camera.js`: `createCamera(...)` orbit with damping, follow/overview/manual modes.

- [ ] **Step 1:** Move existing logic into modules without behaviour change; `board3d.js` re-exports. Run the Playwright smoke (`tools/web-smoke`) — expect canvas still renders (baseline).
- [ ] **Step 2:** Replace `MeshStandardMaterial` with `MeshPhysicalMaterial` (clearcoat for tokens), add procedural env map via `PMREMGenerator` from a gradient scene built from `theme.scene.sky`, `scene.fog`, `renderer.shadowMap` PCFSoft with a directional light shadow (`mapSize` 2048 high / disabled low), felt plane under the board, tile extrude with beveled top and emissive strip of district colour (`theme.css['--glow']` intensity).
- [ ] **Step 3:** Implement `setTheme(json)`: recolour materials in place (no rebuild), update lights/fog/sky. Implement `setQuality`: toggles `renderer.shadowMap.enabled`, pixel ratio cap (2 high / 1 low).
- [ ] **Step 4:** Damped orbit (inertia), pinch-zoom and two-finger pan via pointer events (track active pointers map), clamp radius/phi; dispose removes every listener (keep `cleanup` array).
- [ ] **Step 5:** Manual check: `cd tools/web-smoke` run existing script; screenshot all four themes via `setTheme`. Commit `feat(web): modular PBR 3D scene with theming`.

---

### Task 4: 3D motion (hops, buildings, dice, auras, camera)

**Files:**
- Create: `src/Game.Web/wwwroot/js/3d/motion.js`
- Modify: `src/Game.Web/wwwroot/js/3d/scene.js`, `board3d.js`

**Interfaces:**
- Produces in `motion.js`: `class Tween { constructor(duration, ease, onUpdate, onDone) }`, `Motion.update(dt)`, `Motion.hop(token, fromTile, toTile, perStep)`, `Motion.spring(mesh, from, to)`, `Motion.dice(group, d1, d2)`. `playEvent` dispatches on `e['$t']` using the same names as `Cues` (PlayerMoved, DiceRolled, BuildingConstructed, PropertyPurchased, PlayerSentToJail, PlayerBankrupt, GameEnded).
- Reduce-motion: all tweens complete instantly.

- [ ] **Step 1:** Implement `Tween`/`Motion` with a single registered update list stepped from the render loop using a clock `dt`.
- [ ] **Step 2:** Token move: per-tile arc hop (sin y-offset 0.35), squash on landing; walks every intermediate tile between `From` and `To` (wrap at board length), so a roll visibly steps.
- [ ] **Step 3:** Dice: two physically-lite cubes tumble above the centre for 0.8 s then rest on faces `d1`,`d2` (precomputed face-up quaternions); hidden when idle.
- [ ] **Step 4:** Buildings spring from scale 0 with overshoot; level 5 gets a distinct tower mesh; ownership aura = additive ring under the tile coloured by owner hue; mortgaged tiles desaturate + lower emissive.
- [ ] **Step 5:** Camera: on `TurnStarted` follow the active player's token (ease), on `AuctionStarted`/`Trade*` ease to overview, user drag switches to manual for 4 s then resumes auto.
- [ ] **Step 6:** Verify with a headless bot match in the smoke script (token moves, canvas non-blank). Commit `feat(web): animated 3D tokens, dice, buildings and camera`.

---

### Task 5: Effects, post-processing and quality tiers

**Files:**
- Create: `src/Game.Web/wwwroot/js/fx.js`, `src/Game.Web/wwwroot/js/vendor/postprocessing/*` (EffectComposer, RenderPass, UnrealBloomPass, ShaderPass, CopyShader, LuminosityHighPassShader, OutputPass at the vendored three.js version), `.../vendor/postprocessing/LICENSE`
- Modify: `3d/scene.js`, `board3d.js`

**Interfaces:**
- Produces: `fx.js` exports `createFx(THREE, scene, renderer, camera, theme, quality) -> {particles.burst(pos, kind), particles.trail(from, to, kind), render(dt), setQuality(q), dispose()}`; `probeQuality(renderFrame, frames=20) -> Promise<"high"|"low">`. Bloom/vignette modules are imported dynamically only when quality is "high".

- [ ] **Step 1:** Particle system: pooled `THREE.Points` (cap 400 high / 80 low), kinds `cash` (gold coins upward), `build` (dust ring), `win` (confetti), `trail` (money flowing between players on `RentPaid`/`MoneyTransferred`).
- [ ] **Step 2:** Lazy `import('./vendor/postprocessing/...')` for composer + bloom (strength from `theme.scene.bloom`) + vignette; on failure silently render without.
- [ ] **Step 3:** `probeQuality`: render N frames, average dt; > 28 ms -> low. Auto mode applies it once after init and on `devicePixelRatio`/size change; also default low when `matchMedia('(max-width:700px)')` or `navigator.hardwareConcurrency <= 4`.
- [ ] **Step 4:** Burst handling: `playEvent` receives a flag `burst` (pacer backlog > 12) -> skip particles, still apply state.
- [ ] **Step 5:** Check bundle growth (`du -sh src/Game.Web/wwwroot`) <= budget; manual high vs low screenshots. Commit `feat(web): particles, bloom and quality tiers`.

---

### Task 6: Audio engine

**Files:**
- Create: `src/Game.Web/wwwroot/js/audio.js`
- Create: `src/Game.Web/Components/AudioBridge.razor`
- Modify: `src/Game.Web/Pages/Play.razor`

**Interfaces:**
- Consumes: `Cues.For(e).Sound` ids: `dice hop buy sell build cash jail card hammer trade bankrupt win chime`; theme `sound` block.
- Produces: `audio.js` exports `init(themeJson, settingsJson)`, `setTheme`, `setVolumes(master, sfx, music, muted)`, `play(soundId, intensity)`, `startAmbient()`, `stopAmbient()`, `dispose()`. All functions are no-ops when `AudioContext` is unavailable; context created lazily and `resume()`d on first `pointerdown`/`keydown`.
- `AudioBridge.razor`: subscribes to `Game.EventPlayed`, calls `audio.play(Cues.For(e).Sound)`; skips when backlog burst.

- [ ] **Step 1:** Implement synth voices with oscillators + gain envelopes (no files): dice = filtered noise clicks sequence; hop = short pitched blip rising with tile index; cash = two-note arpeggio; build = noise thump + pitch; jail = low descending; hammer = noise burst + low sine; win = major arpeggio; chime = single bell (additive partials). Pitches derive from `theme.sound.root`/`wave`.
- [ ] **Step 2:** Ambient: slow generative pad (2 detuned oscillators through lowpass, random note walk on a pentatonic scale every 2-4 s), gated by `Music` volume.
- [ ] **Step 3:** `AudioBridge` + Settings wiring (volumes persisted); mute button in HUD.
- [ ] **Step 4:** Playwright: stub `AudioContext` to count `createOscillator` calls; assert > 0 after a dice roll, 0 when muted. Commit `feat(web): synthesized themed sound engine`.

---

### Task 7: 2D board redesign

**Files:**
- Modify: `src/Game.Web/Components/Board2D.razor`, `src/Game.Web/wwwroot/css/board2d.css`
- Create: `src/Game.Web/Components/DiceTray.razor`, `src/Game.Web/wwwroot/js/board2d-fx.js`

- [ ] **Step 1:** Convert tiles to richer cards: district colour band, procedural pattern (CSS gradients from theme tokens), icon glyph per tile type (railway/utility/chance/jail), owner ring, building pips -> small SVG houses/tower.
- [ ] **Step 2:** Tokens as absolutely positioned elements animated with CSS transitions along the tile path (use Web Animations API per hop from `board2d-fx.js` receiving `PlayerMoved`), squash on landing; reduced-motion disables.
- [ ] **Step 3:** `DiceTray`: animated dice faces (CSS 3D cube) rolling then settling to `Die1`/`Die2`.
- [ ] **Step 4:** Canvas overlay for particles (cash/confetti) using the same cue kinds as 3D; subscribe via `Game.EventPlayed`.
- [ ] **Step 5:** Run `dotnet build`, smoke 2D, screenshot all themes. Commit `feat(web): animated themed 2D board`.

---

### Task 8: Interaction layer

**Files:**
- Create: `src/Game.Web/Components/{PropertyCard,Toasts,EventLog,SettingsSheet,TurnBanner}.razor`, `src/Game.Web/wwwroot/js/keys.js`
- Modify: `Hud.razor`, `PropertyPanel.razor`, `TradeDialog.razor`, `Pages/Play.razor`

- [ ] **Step 1:** `PropertyCard`: flip-in modal/sheet for the selected tile (rent table, owner, buy/mortgage/build actions via existing command paths in `Hud`/`PropertyPanel`); closes on Esc/backdrop.
- [ ] **Step 2:** `Toasts` (from `CommandRejected` and key events), `EventLog` drawer (last 100 human-readable events), `TurnBanner` ("Your turn" slide-in).
- [ ] **Step 3:** `keys.js`: Space roll, B buy, E end turn, Esc close, `M` mute; only active when no input is focused; Blazor handles via `DotNetObjectReference`.
- [ ] **Step 4:** `SettingsSheet`: theme picker (live), quality, volumes, mute, reduce motion, 2D/3D; bound to `SettingsService`.
- [ ] **Step 5:** Smoke: open card by clicking tile, switch theme live, toggle 2D/3D. Commit `feat(web): property cards, toasts, log, shortcuts and settings`.

---

### Task 9: Mobile layout and PWA

**Files:**
- Modify: `css/layout.css`, `Pages/Play.razor`, `Hud.razor`, `index.html`
- Create: `wwwroot/manifest.webmanifest`, `wwwroot/icons/{192,512,maskable}.png`

- [ ] **Step 1:** `<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">`, `theme-color`, manifest link; icons generated from a themed SVG via a small script (`tools/` one-off, committed output only).
- [ ] **Step 2:** Under 800 px: board fills `100dvh - sheet height`; HUD becomes a bottom sheet (collapsed 72 px with primary action, drag/tap to expand); safe-area padding via `env(safe-area-inset-*)`; landscape: side rail.
- [ ] **Step 3:** All buttons/inputs min 44x44; verify no horizontal scroll at 360 px.
- [ ] **Step 4:** Playwright mobile device profile (e.g. Pixel 7): play a turn with taps; assert no horizontal overflow. Commit `feat(web): mobile layout and PWA manifest`.

---

### Task 10: Home and Lobby redesign

**Files:**
- Modify: `Pages/Home.razor`, `Pages/Lobby.razor`, `css/components.css`
- Create: `src/Game.Web/Components/HeroBoard.razor`

- [ ] **Step 1:** `HeroBoard`: reuses `board3d.js` with a built-in demo board and auto-orbit, 30 fps cap, paused when tab hidden or `Low` quality (falls back to a CSS gradient + animated 2D tiles).
- [ ] **Step 2:** Home: layered hero, glass cards for Play vs bots / Online / Rooms, theme picker chips, name field; motion on hover/press.
- [ ] **Step 3:** Lobby: seat cards with avatar glyphs, bot level badges, ready pulse, rule summary; keep all existing handlers.
- [ ] **Step 4:** Smoke: Home -> bots -> board. Commit `feat(web): redesigned home and lobby`.

---

### Task 11: Verification, tests and release

**Files:**
- Modify: `tools/web-smoke/*`, `tools/set-version.sh` usage

- [ ] **Step 1:** Extend Playwright smoke: theme switch applies `data-theme`; 2D/3D toggle; canvas pixel sample non-uniform; mobile viewport run; mute/quality toggles; no console errors.
- [ ] **Step 2:** Run `dotnet test BoardEmpire.sln` and the smoke; capture desktop+mobile screenshots for each theme into `docs/screenshots/` (small, committed only if < 2 MB total).
- [ ] **Step 3:** Check load size and a throttled-CPU frame time; document in `HANDOFF.md`.
- [ ] **Step 4:** Ask the user before any version bump/tag/push (release rules require explicit confirmation). Commit `chore(web): studio redesign verification`.

---

## Self-Review

- **Spec coverage:** Themes (T2,T3), 3D graphics (T3-T5), 2D (T7), quality tiers (T5), interaction/shortcuts/reduced-motion (T4,T8), sound (T6), mobile+PWA (T9), screens (T10), pacer/cue timings (T1), testing (T1,T2,T6,T9,T11), delivery (T11). No gaps.
- **Placeholders:** none; Task 1 notes the one thing to confirm (event constructor arities) because those are read from source at execution time.
- **Type consistency:** sound ids in T1 match T6; `optsJson`/`setTheme`/`setQuality` names consistent across T3-T5; settings keys consistent T2/T8.
- **Review Focus:** theme fallback (T2 tests), localStorage failure (T2 step 4), AudioContext absence (T6 step 1), burst collapsing (T1 test, T5 step 4), dispose/resize leaks (T3 step 4).
